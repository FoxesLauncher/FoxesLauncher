Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Media.Imaging

''' <summary>
''' 启动时显示的 FXL 图标。
''' 使用 WPF 的设备无关单位（DIP）绘制，尺寸会自动跟随系统 DPI 缩放。
''' </summary>
Public Class SplashWindow
    Inherits Window

    ''' <summary>
    ''' 图标边长（DIP）。100% 缩放时为 128 像素，150% 时为 192 像素，以此类推。
    ''' </summary>
    Public Const LogoSize As Double = 128

    Public Sub New()
        'WPF 的 SplashScreen 只会显示 ICO 的第一帧（16×16），这里改用独立窗口显示最高分辨率的一帧
        WindowStyle = WindowStyle.None
        ResizeMode = ResizeMode.NoResize
        AllowsTransparency = True
        Background = Brushes.Transparent
        ShowInTaskbar = False
        Topmost = True
        SizeToContent = SizeToContent.WidthAndHeight
        WindowStartupLocation = WindowStartupLocation.CenterScreen
        Focusable = False
        ShowActivated = False
        UseLayoutRounding = True
        SnapsToDevicePixels = True

        '从 ICO 中取出分辨率最高的一帧，保证高 DPI 下清晰
        Dim Decoder As New IconBitmapDecoder(
            New Uri("pack://application:,,,/Images/icon.ico", UriKind.Absolute),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
        Dim IconFrame As BitmapFrame = Decoder.Frames(0)
        For Each Frame As BitmapFrame In Decoder.Frames
            If Frame.PixelWidth > IconFrame.PixelWidth Then IconFrame = Frame
        Next
        IconFrame.Freeze()

        Dim Logo As New Image With {
            .Source = IconFrame,
            .Width = LogoSize,
            .Height = LogoSize,
            .Stretch = Stretch.Uniform
        }
        RenderOptions.SetBitmapScalingMode(Logo, BitmapScalingMode.HighQuality)
        Content = Logo
        Opacity = 0
    End Sub

    ''' <summary>
    ''' 显示启动图标。
    ''' </summary>
    Public Sub ShowSplash(FadeIn As Boolean)
        Show()
        If FadeIn Then
            BeginAnimation(OpacityProperty, New DoubleAnimation(0, 1, New Duration(TimeSpan.FromMilliseconds(200))))
        Else
            BeginAnimation(OpacityProperty, Nothing)
            Opacity = 1
        End If
    End Sub

    ''' <summary>
    ''' 淡出后关闭启动图标。
    ''' </summary>
    Public Sub FadeOutAndClose(FadeOutDuration As TimeSpan)
        If Not IsVisible OrElse FadeOutDuration <= TimeSpan.Zero Then
            CloseNow()
            Return
        End If
        Dim FadeOut As New DoubleAnimation(Opacity, 0, New Duration(FadeOutDuration))
        AddHandler FadeOut.Completed, Sub(sender As Object, e As EventArgs) CloseNow()
        BeginAnimation(OpacityProperty, FadeOut)
    End Sub

    Private Sub CloseNow()
        MyBase.Close()
    End Sub

End Class
