Public Class PageSetupSystem


    Private Sub PageSetupSystem_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        InitRegionLanguageCombos()
        AniControlEnabled += 1
        Reload()
        SliderLoad()
        AniControlEnabled -= 1
        '切换语言后就地更新区域名称（区域名称是翻译文本，且下拉框选项不会自动刷新）
        AddHandler ModLanguage.LanguageChanged, AddressOf OnLanguageChanged

    End Sub
    ''' <summary>
    ''' 界面语言切换后，更新下拉框中随语言变化的区域名称，不改变当前选择。
    ''' </summary>
    Private Sub OnLanguageChanged(Language As String)
        Try
            Dim SelectedCode As String = GetSelectedRegionCode()
            '重建期间不触发设置保存，避免与正在进行的语言切换相互干扰
            AniControlEnabled += 1
            Try
                RebuildRegionItems(SelectedCode)
            Finally
                AniControlEnabled -= 1
            End Try
        Catch ex As Exception
            Logger.Warn(ex, "刷新区域与语言下拉框文本失败")
        End Try
    End Sub
    ''' <summary>
    ''' 获取当前选择的区域代码，若没有选择则返回空字符串。
    ''' </summary>
    Private Function GetSelectedRegionCode() As String
        Dim Item = TryCast(ComboRegion.SelectedItem, MyComboBoxItem)
        If Item IsNot Nothing AndAlso Item.Tag IsNot Nothing Then Return Item.Tag.ToString()
        Return ""
    End Function
    ''' <summary>
    ''' 重建区域下拉框选项，并尽量保持原来的选择。区域名称是翻译文本，切换语言后需要重建。
    ''' </summary>
    Private Sub RebuildRegionItems(Optional SelectedCode As String = Nothing)
        If String.IsNullOrEmpty(SelectedCode) Then SelectedCode = GetSelectedRegionCode()
        If String.IsNullOrEmpty(SelectedCode) Then SelectedCode = CurrentRegion
        ComboRegion.Items.Clear()
        Dim Entries = RegionEntries
        For Each Entry In Entries
            Dim Item As New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code}
            SettingService.SetValue(Item, Entry.Code)
            ComboRegion.Items.Add(Item)
        Next
        ComboRegion.SelectedIndex = Math.Max(0, Entries.FindIndex(Function(e) e.Code = SelectedCode))
    End Sub
    ''' <summary>
    ''' 初始化区域与语言下拉框的选项。
    ''' </summary>
    Private Sub InitRegionLanguageCombos()
        If ComboRegion.Items.Count = 0 Then
            Dim Entries = RegionEntries
            For Each Entry In Entries
                Dim Item As New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code}
                SettingService.SetValue(Item, Entry.Code)
                ComboRegion.Items.Add(Item)
            Next
        End If
        If ComboLanguage.Items.Count = 0 Then
            For Each Entry In LanguageEntries
                Dim Item As New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code}
                SettingService.SetValue(Item, Entry.Code)
                ComboLanguage.Items.Add(Item)
            Next
        End If
    End Sub
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化启动器页设置")
            Hint(T("已初始化启动器页设置！"), HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    '滑动条
    Private Sub SliderLoad()
        SliderDownloadThread.GetHintText = Function(v) v + 1
        SliderDownloadSpeed.GetHintText =
        Function(v)
            Select Case v
                Case Is <= 14
                    Return (v + 1) * 0.1 & " M/s"
                Case Is <= 31
                    Return (v - 11) * 0.5 & " M/s"
                Case Is <= 41
                    Return (v - 21) & " M/s"
                Case Else
                    Return T("无限制")
            End Select
        End Function
        SliderDebugAnim.GetHintText = Function(v) If(v > 29, T("关闭"), (v / 10 + 0.1) & "x")
    End Sub
    Private Sub SliderDownloadThread_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderDownloadThread.PreviewChange
        If SliderDownloadThread.Value < 100 Then Return
        If Not Settings.Get(Of Boolean)("HintDownloadThread") Then
            Settings.Set("HintDownloadThread", True)
            MyMsgBox(T("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。") & vbCrLf &
                     T("一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！"), T("警告"), T("我知道了"), IsWarn:=True)
        End If
    End Sub

    '调试模式
    Private Sub CheckDebugMode_Change() Handles CheckDebugMode.Change
        If AniControlEnabled = 0 Then Hint(T("部分调试信息将在刷新或启动器重启后切换显示！"),, False)
    End Sub

    Private Sub BtnSystemUpdate_Click(sender As Object, e As EventArgs) Handles BtnSystemUpdate.Click
        UpdateCheckByButton()
    End Sub
    ''' <summary>
    ''' 启动器是否已经是最新版？
    ''' 若返回 Nothing，则代表无更新缓存文件或出错。
    ''' </summary>
    Public Shared Function IsLauncherNewest() As Boolean?
        Try
            '确认服务器公告是否正常
            Dim ServerContent As String = If(FileUtils.TryReadAsString(PathTemp & "Cache\Notice.cfg"), "")
            If ServerContent.Split("|").Count < 3 Then Return Nothing
            '确认是否为最新
            Return ServerContent.Split("|")(If(BuildType = BuildTypes.Release, 2, 1)) <= VersionCode
        Catch ex As Exception
            Logger.Error(ex, "确认启动器更新失败")
            Return Nothing
        End Try
    End Function

#Region "导出 / 导入设置"

    Private Sub BtnSystemSettingExp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingExp.Click
        Hint(T("该功能尚在开发中！"))
    End Sub
    Private Sub BtnSystemSettingImp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingImp.Click
        Hint(T("该功能尚在开发中！"))
    End Sub

#End Region

End Class
