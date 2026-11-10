using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Backdrop
{
    // Мини-среда для скриптов текстовых слоёв Wallpaper Engine (часы, дата и т.п.)
    // на движке Chakra, который встроен в Windows (chakra.dll). Работает только в потоке интерфейса.
    class ScriptHost : IDisposable
    {
        [DllImport("chakra.dll")] static extern int JsCreateRuntime(int attributes, IntPtr threadService, out IntPtr runtime);
        [DllImport("chakra.dll")] static extern int JsCreateContext(IntPtr runtime, out IntPtr context);
        [DllImport("chakra.dll")] static extern int JsSetCurrentContext(IntPtr context);
        [DllImport("chakra.dll", CharSet = CharSet.Unicode)] static extern int JsRunScript(string script, IntPtr sourceContext, string sourceUrl, out IntPtr result);
        [DllImport("chakra.dll")] static extern int JsConvertValueToString(IntPtr value, out IntPtr str);
        [DllImport("chakra.dll")] static extern int JsStringToPointer(IntPtr value, out IntPtr chars, out UIntPtr length);
        [DllImport("chakra.dll")] static extern int JsDisposeRuntime(IntPtr runtime);
        [DllImport("chakra.dll")] static extern int JsGetAndClearException(out IntPtr exception);

        IntPtr runtime, context;
        int counter;
        public bool Ok { get; private set; }

        const string Prelude = @"
var console = { log: function(){}, warn: function(){}, error: function(){} };
var localStorage = { get: function(){ return undefined; }, set: function(){}, remove: function(){}, clear: function(){} };
var __start = Date.now();
var engine = {
  frametime: 1/60, get runtime(){ return (Date.now() - __start) / 1000; },
  get timeOfDay(){ var d = new Date(); return (d.getHours()*3600 + d.getMinutes()*60 + d.getSeconds()) / 86400; },
  screenResolution: { x: 1920, y: 1080 }, canvasSize: { x: 1920, y: 1080 },
  isDesktopDevice: function(){ return true; }, isMobileDevice: function(){ return false; }, isWallpaper: function(){ return true; },
  isScreensaver: function(){ return false; }, isRunningInEditor: function(){ return false; },
  registerAudioBuffers: function(){ return { left: [], right: [], average: [] }; },
  setTimeout: function(){}, setInterval: function(){}
};
var input = { cursorWorldPosition: { x: 0, y: 0 }, cursorScreenPosition: { x: 0, y: 0 }, cursorLeftDown: false };
function __makeProps(p) {
  var o = {};
  var b = {};
  function add(def) {
    var v = def.value;
    if (v === undefined && def.options && def.options.length) v = def.options[0].value;
    if (p && Object.prototype.hasOwnProperty.call(p, def.name)) v = p[def.name];
    o[def.name] = v; return b;
  }
  ['addCheckbox','addCombo','addSlider','addText','addTextInput','addColor','addFile','addDirectory','addSoundFile','addImageFile','addVideoFile'].forEach(function(n){ b[n] = add; });
  b.finish = function(){ return o; };
  return b;
}
";

        public ScriptHost()
        {
            try
            {
                if (JsCreateRuntime(0, IntPtr.Zero, out runtime) != 0) return;
                if (JsCreateContext(runtime, out context) != 0) return;
                if (JsSetCurrentContext(context) != 0) return;
                IntPtr r;
                Ok = JsRunScript(Prelude, IntPtr.Zero, "prelude", out r) == 0;
                JsSetCurrentContext(IntPtr.Zero);
            }
            catch (Exception e) { Log.Write("Скрипты недоступны: " + e.Message); Ok = false; }
        }

        // Регистрирует скрипт слоя; возвращает имя объекта в JS или null
        public string Add(string script, Dictionary<string, object> props)
        {
            if (!Ok || string.IsNullOrEmpty(script)) return null;
            string name = "__layer" + (++counter);
            string body = Regex.Replace(script, @"^\s*import\s[^\n]*$", "", RegexOptions.Multiline);
            body = Regex.Replace(body, @"\bexport\s+(default\s+)?", "");
            string json = new JavaScriptSerializer().Serialize(props ?? new Dictionary<string, object>());
            var sb = new StringBuilder();
            sb.Append("var ").Append(name).Append(" = (function(){\n");
            sb.Append("var createScriptProperties = function(){ return __makeProps(").Append(json).Append("); };\n");
            sb.Append(body);
            sb.Append("\n;return { update: (typeof update === 'function') ? update : null, init: (typeof init === 'function') ? init : null };\n})();");
            string err;
            if (Run(sb.ToString(), out err) == null) { Log.Write("Скрипт слоя не запустился: " + err); return null; }
            return name;
        }

        public string Call(string layer, string fn, string value)
        {
            if (layer == null) return null;
            string arg = new JavaScriptSerializer().Serialize(value ?? "");
            string err;
            return Run("(function(){ var f = " + layer + "." + fn + "; if (!f) return null; var r = f(" + arg + "); return r === undefined || r === null ? " + arg + " : String(r); })()", out err);
        }

        string Run(string code, out string error)
        {
            error = null;
            if (!Ok) return null;
            try
            {
                JsSetCurrentContext(context);
                IntPtr res;
                int rc = JsRunScript(code, IntPtr.Zero, "", out res);
                if (rc != 0)
                {
                    error = "код " + rc.ToString("X");
                    IntPtr ex; JsGetAndClearException(out ex); // иначе контекст «застрянет» в состоянии ошибки
                    return null;
                }
                IntPtr str, chars; UIntPtr len;
                if (JsConvertValueToString(res, out str) != 0) return null;
                if (JsStringToPointer(str, out chars, out len) != 0) return null;
                return Marshal.PtrToStringUni(chars, (int)len.ToUInt32());
            }
            catch (Exception e) { error = e.Message; return null; }
            finally { try { JsSetCurrentContext(IntPtr.Zero); } catch { } }
        }

        public void Dispose()
        {
            if (runtime != IntPtr.Zero)
            {
                try { JsSetCurrentContext(IntPtr.Zero); JsDisposeRuntime(runtime); } catch { }
                runtime = IntPtr.Zero; Ok = false;
            }
        }
    }
}
