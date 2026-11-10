using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace Vivid
{
    // Стиль «кинотеатр»: живые обои на весь фон окна, спокойные стеклянные элементы и ОДИН акцентный цвет,
    // который пользователь выбирает сам (Настройки → Цвет). Никаких градиентов.
    static class Theme
    {
        public static readonly string[] Presets = { "#FF6B4A", "#FF3B5C", "#FFB020", "#F2F0EC", "#3DDC97", "#2EC5FF", "#4D7CFF", "#9B6BFF", "#FF5CC8", "#C8A27A" };
        public static string AccentHex = "#FF6B4A";
        public static Color AccentColor = Color.FromRgb(0xFF, 0x6B, 0x4A);
        public static Brush Accent = Freeze(new SolidColorBrush(AccentColor));
        static string A2 { get { return AccentHex; } }

        // Текст поверх акцента: тёмный на светлых цветах, белый на тёмных
        public static Brush OnAccent { get { double l = 0.299 * AccentColor.R + 0.587 * AccentColor.G + 0.114 * AccentColor.B; return l > 165 ? Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C))) : Brushes.White; } }
        static string OnAccentHex { get { double l = 0.299 * AccentColor.R + 0.587 * AccentColor.G + 0.114 * AccentColor.B; return l > 165 ? "#0A0A0C" : "#FFFFFF"; } }

        public static bool SetAccent(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = 255;
                AccentColor = c;
                AccentHex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                Accent = Freeze(new SolidColorBrush(c));
                return true;
            }
            catch { return false; }
        }
        public static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0x07, 0x07, 0x09)));
        public static readonly Brush Glass = Freeze(new SolidColorBrush(Color.FromArgb(0x8C, 0x10, 0x10, 0x14)));
        public static readonly Brush GlassStrong = Freeze(new SolidColorBrush(Color.FromArgb(0xEB, 0x0C, 0x0C, 0x10)));
        public static readonly Brush GlassLine = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
        public static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)));
        public static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xF7, 0xF2)));
        public static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFA, 0xF7, 0xF2)));
        public static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xFA, 0xF7, 0xF2)));
        public static readonly Brush Good = Freeze(new SolidColorBrush(Color.FromRgb(0x5E, 0xF0, 0xA0)));
        public static readonly Brush Warn = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x4D)));
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Head = new FontFamily("Bahnschrift, Segoe UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");


        static Brush Freeze(Brush b) { b.Freeze(); return b; }

        // Разрядка для заголовков (в WPF нет letter-spacing)
        public static string Spaced(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(s[i]); }
            return sb.ToString();
        }

        static string Grad { get { return "<SolidColorBrush Color='" + AccentHex + "'/>"; } }

        // Кнопка-«пилюля»: подсветка при наведении — белая вуаль поверх любого фона (и градиента тоже)
        static string ButtonStyle(string key, string bgXaml, string border, string fg, string radius, string weight)
        {
            return @"
  <Style x:Key='" + key + @"' TargetType='Button'>
    <Setter Property='Foreground' Value='" + fg + @"'/>
    <Setter Property='Background'><Setter.Value>" + bgXaml + @"</Setter.Value></Setter>
    <Setter Property='BorderBrush' Value='" + border + @"'/>
    <Setter Property='Padding' Value='18,10'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='FontWeight' Value='" + weight + @"'/>
    <Setter Property='FontFamily' Value='Bahnschrift'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Grid>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='" + radius + @"'/>
          <Border x:Name='hl' Background='#FFFFFF' Opacity='0' CornerRadius='" + radius + @"'/>
          <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.10'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.18'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
  </Style>";
        }

        public static ResourceDictionary Build()
        {
            string x = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"
            + ButtonStyle("AccentBtn", Grad, "Transparent", OnAccentHex, "24", "SemiBold")
            + ButtonStyle("Btn", "<SolidColorBrush Color='#8C101014'/>", "#30FFFFFF", "#FAF7F2", "24", "Normal")
            + ButtonStyle("GhostBtn", "<SolidColorBrush Color='Transparent'/>", "Transparent", "#E6FAF7F2", "24", "Normal")
            + ButtonStyle("RoundBtn", "<SolidColorBrush Color='#8C101014'/>", "#30FFFFFF", "#FAF7F2", "20", "Normal")
            + @"
  <Style x:Key='Chip' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='#B0FAF7F2'/>
    <Setter Property='FontFamily' Value='Bahnschrift'/>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RadioButton'>
        <Border x:Name='b' Background='#66101014' BorderBrush='#26FFFFFF' BorderThickness='1' CornerRadius='16' Padding='13,6' Margin='0,0,6,0'>
          <ContentPresenter VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#60FFFFFF'/><Setter Property='Foreground' Value='#FAF7F2'/></Trigger>
          <Trigger Property='IsChecked' Value='True'><Setter TargetName='b' Property='Background' Value='#FAF7F2'/><Setter TargetName='b' Property='BorderBrush' Value='#FAF7F2'/><Setter Property='Foreground' Value='#0A0A0C'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='Seg' TargetType='RadioButton' BasedOn='{StaticResource Chip}'/>

  <Style x:Key='Switch' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#FAF7F2'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='CheckBox'>
        <Border Background='Transparent' Padding='0,7'>
          <DockPanel LastChildFill='True'>
            <Grid DockPanel.Dock='Right' Width='34' Height='18' Margin='14,0,0,0' VerticalAlignment='Center'>
              <Border x:Name='track' CornerRadius='9' Background='#33FFFFFF'/>
              <Border x:Name='on' CornerRadius='9' Opacity='0'><Border.Background>" + Grad + @"</Border.Background></Border>
              <Ellipse x:Name='dot' Width='12' Height='12' Fill='#FAF7F2' HorizontalAlignment='Left' Margin='3,0'/>
            </Grid>
            <ContentPresenter VerticalAlignment='Center'/>
          </DockPanel>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='on' Property='Opacity' Value='1'/>
            <Setter TargetName='dot' Property='HorizontalAlignment' Value='Right'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Sl' TargetType='Slider'>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Slider'>
        <Grid Height='26' Background='Transparent'>
          <Border Height='4' CornerRadius='2' Background='#26FFFFFF' VerticalAlignment='Center'/>
          <Track x:Name='PART_Track'>
            <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' VerticalAlignment='Center'><Border.Background>" + Grad + @"</Border.Background></Border></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Focusable='False'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Width='18' Height='18' Fill='#FAF7F2' Cursor='Hand'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style x:Key='Search' TargetType='TextBox'>
    <Setter Property='Foreground' Value='#FAF7F2'/>
    <Setter Property='CaretBrush' Value='#FAF7F2'/>
    <Setter Property='SelectionBrush' Value='" + A2 + @"'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='TextBox'>
        <Border x:Name='b' Background='#8C101014' BorderBrush='#30FFFFFF' BorderThickness='1' CornerRadius='18' Padding='34,7,12,7'>
          <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#90FFFFFF'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='6'/>
    <Setter Property='MinWidth' Value='6'/>
    <Setter Property='Height' Value='6'/>
    <Setter Property='MinHeight' Value='6'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Track x:Name='PART_Track' IsDirectionReversed='True'>
          <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#40FFFFFF' Margin='1'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track>
        <ControlTemplate.Triggers>
          <Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#FAF7F2'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ToolTip'>
        <Border Background='#F0141418' BorderBrush='#30FFFFFF' BorderThickness='1' CornerRadius='10' Padding='10,6'><ContentPresenter/></Border>
      </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(x);
        }
    }
}
