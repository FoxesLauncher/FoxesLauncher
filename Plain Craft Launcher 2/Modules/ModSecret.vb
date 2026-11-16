'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '构建时配置（VersionBranchMain、RegFolder、OAuthClientId、CurseForgeAPIKey、ClsBaseUrl、CommitHash）
    '由 build\GenerateSecrets.ps1 根据仓库根目录的 .env 生成到 Modules\GeneratedSecrets.vb，
    '请不要在此处硬编码；配置格式参考 .env.example。

#Region "网络鉴权"

    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题与深色模式"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ColorHue As Integer = 210, ColorSat As Integer = 85

    ''' <summary>
    ''' 判断系统当前是否使用深色模式。
    ''' </summary>
    Private Function IsSystemDarkTheme() As Boolean
        Try
            Return CInt(RegistryUtils.TryRead("HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) = 0
        Catch
            Return False
        End Try
    End Function
    ''' <summary>
    ''' 根据设置（开 / 跟随系统 / 关）判断当前是否启用深色模式。
    ''' </summary>
    Public Function IsDarkModeEnabled() As Boolean
        Select Case Settings.Get(Of Integer)("UiDarkMode")
            Case 0
                Return False
            Case 1
                Return True
            Case Else
                Return IsSystemDarkTheme()
        End Select
    End Function

    Public Sub ThemeRefresh()
        Try
            Dim IsDark As Boolean = IsDarkModeEnabled()
            If IsDark Then
                Color1 = New MyColor(232, 234, 238)
                Color2 = New MyColor(74, 130, 220)
                Color3 = New MyColor(96, 156, 246)
                Color4 = New MyColor(126, 178, 252)
                Color5 = New MyColor(74, 104, 150)
                Color6 = New MyColor(55, 63, 78)
                Color7 = New MyColor(38, 42, 51)
                Color8 = New MyColor(30, 33, 40)
                ColorBg0 = New MyColor(98, 140, 200)
                ColorBg1 = New MyColor(230, 44, 48, 58)
                ColorGray1 = New MyColor(232, 234, 238)
                ColorGray4 = New MyColor(128, 134, 146)
                ColorGray5 = New MyColor(82, 88, 100)
            Else
                Color1 = New MyColor(52, 61, 74)
                Color2 = New MyColor(11, 91, 203)
                Color3 = New MyColor(19, 112, 243)
                Color4 = New MyColor(72, 144, 245)
                Color5 = New MyColor(150, 192, 249)
                Color6 = New MyColor(213, 230, 253)
                Color7 = New MyColor(222, 236, 253)
                Color8 = New MyColor(234, 242, 254)
                ColorBg0 = New MyColor(150, 192, 249)
                ColorBg1 = New MyColor(190, Color7)
                ColorGray1 = New MyColor(64, 64, 64)
                ColorGray4 = New MyColor(166, 166, 166)
                ColorGray5 = New MyColor(204, 204, 204)
            End If

            ColorSemiTransparent = New MyColor(1, Color8)
            SetBrush("ColorBrush1", Color1)
            SetBrush("ColorBrush2", Color2)
            SetBrush("ColorBrush3", Color3)
            SetBrush("ColorBrush4", Color4)
            SetBrush("ColorBrush5", Color5)
            SetBrush("ColorBrush6", Color6)
            SetBrush("ColorBrush7", Color7)
            SetBrush("ColorBrush8", Color8)
            SetBrush("ColorBrushBg0", ColorBg0)
            SetBrush("ColorBrushBg1", ColorBg1)
            SetBrush("ColorBrushSemiTransparent", ColorSemiTransparent)
            SetBrush("ColorBrushCard", If(IsDark, New MyColor(242, 38, 42, 51), New MyColor(245, 255, 255, 255)))
            SetBrush("ColorBrushDialog", If(IsDark, New MyColor(255, 42, 46, 56), New MyColor(255, 251, 251, 251)))
            SetBrush("ColorBrushInput", If(IsDark, New MyColor(255, 31, 34, 41), New MyColor(255, 255, 255, 255)))
            SetBrush("ColorBrushHalfWhite", If(IsDark, New MyColor(34, 255, 255, 255), New MyColor(85, 255, 255, 255)))
            SetBrush("ColorBrushSemiWhite", If(IsDark, New MyColor(51, 255, 255, 255), New MyColor(187, 255, 255, 255)))
            SetBrush("ColorBrushBackgroundTransparentSidebar", If(IsDark, New MyColor(241, 32, 35, 42), New MyColor(241, 255, 255, 255)))
            SetBrush("ColorBrushRedBack", If(IsDark, New MyColor(128, 63, 37, 41), New MyColor(128, 251, 221, 221)))
            SetResource("ColorBrushGray1", If(IsDark, New MyColor(232, 234, 238), New MyColor(64, 64, 64)))
            SetResource("ColorBrushGray2", If(IsDark, New MyColor(200, 204, 212), New MyColor(115, 115, 115)))
            SetResource("ColorBrushGray3", If(IsDark, New MyColor(168, 174, 184), New MyColor(140, 140, 140)))
            SetResource("ColorBrushGray4", If(IsDark, New MyColor(128, 134, 146), New MyColor(166, 166, 166)))
            SetResource("ColorBrushGray5", If(IsDark, New MyColor(82, 88, 100), New MyColor(204, 204, 204)))
            SetResource("ColorBrushGray6", If(IsDark, New MyColor(48, 51, 58), New MyColor(235, 235, 235)))
            SetResource("ColorBrushGray7", If(IsDark, New MyColor(40, 43, 50), New MyColor(240, 240, 240)))
            SetResource("ColorBrushGray8", If(IsDark, New MyColor(35, 37, 43), New MyColor(245, 245, 245)))
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            Application.Current.Resources("ColorObjectShadow") = If(IsDark, Color.FromArgb(&HB3, 0, 0, 0), Color.FromArgb(&H71, 0, 0, 0))
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub SetBrush(Key As String, Color As MyColor)
        Application.Current.Resources(Key) = New SolidColorBrush(Color)
    End Sub
    Private Sub SetResource(Key As String, Color As MyColor)
        Application.Current.Resources(Key) = New SolidColorBrush(Color)
        Application.Current.Resources(Key.Replace("ColorBrush", "ColorObject")) = CType(Color, Color)
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If Not FrmMain.IsLoaded Then Return
            Dim IsDark As Boolean = IsDarkModeEnabled()
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            If IsDark Then
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue - 8, 32, 34)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue, 36, 40)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + 8, 32, 34)})
            Else
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 48)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 54)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue, ColorSat, 48)})
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Dim BaseLight As Integer = If(IsDark, 28, 91)
                Dim BaseSat As Integer = If(IsDark, 22, ColorSat * 0.8)
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, BaseSat, BaseLight)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, BaseSat, BaseLight)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, BaseSat, BaseLight)})
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = If(IsDark, New MyColor(27, 29, 35), New MyColor(245, 245, 245))
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        Hint("该版本中不包含更新功能……")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
    End Sub

#End Region

#Region "联网配置"

    ''' <summary>
    ''' 联网获取的配置信息。
    ''' 若获取失败或仍在获取中，可能为 Nothing。
    ''' </summary>
    Public ServerConfig As JObject

    Public ServerLoader As New LoaderTask(Of Integer, Integer)("PCL 配置更新", Sub() Logger.Info("该版本中不包含更新通知功能……"), Priority:=ThreadPriority.BelowNormal) With
        {.ReloadTimeout = 1000 * 60 * 60} '超时 1 小时

#End Region

#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
