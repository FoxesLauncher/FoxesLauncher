Public Class MyMsgRegionLanguage

    Private ReadOnly MyConverter As MyMsgBoxConverter
    Private ReadOnly Uuid As Integer = GetUuid()
    ''' <summary>
    ''' 是否正在重建下拉框选项，用于避免重建过程中误触发语言切换。
    ''' </summary>
    Private IsRebuildingItems As Boolean

    Public Sub New(Converter As MyMsgBoxConverter)
        Try

            InitializeComponent()
            Btn1.Name = Btn1.Name & GetUuid()
            MyConverter = Converter
            If Converter.IsWarn Then
                Btn1.ColorType = MyButton.ColorState.Red
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight")
            End If
            ShapeLine.StrokeThickness = GetWPFSize(1)
            '添加区域与语言选项
            RebuildRegionItems()
            RebuildLanguageItems()
            RefreshTexts()
            '在弹窗内切换语言时立即预览界面语言
            AddHandler ComboLanguage.SelectionChanged, AddressOf ComboLanguage_SelectionChanged

        Catch ex As Exception
            Logger.Error(ex, "区域与语言弹窗初始化失败", LogBehavior.Toast)
        End Try
    End Sub

    ''' <summary>
    ''' 重建区域下拉框。区域名称是翻译文本，切换语言后需要重新生成。
    ''' 若未指定代码，优先保留当前选择，其次使用已保存的设置。
    ''' </summary>
    Private Sub RebuildRegionItems(Optional SelectedCode As String = Nothing)
        If String.IsNullOrEmpty(SelectedCode) Then SelectedCode = TryGetSelectedCode(ComboRegion)
        If String.IsNullOrEmpty(SelectedCode) Then SelectedCode = CurrentRegion
        Dim Entries As List(Of RegionLanguageEntry) = RegionEntries
        IsRebuildingItems = True
        Try
            ComboRegion.Items.Clear()
            For Each Entry In Entries
                ComboRegion.Items.Add(New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code})
            Next
            ComboRegion.SelectedIndex = Math.Max(0, Entries.FindIndex(Function(e) e.Code = SelectedCode))
        Finally
            IsRebuildingItems = False
        End Try
    End Sub

    ''' <summary>
    ''' 重建语言下拉框。语言名称来自语言清单，不随界面语言变化。
    ''' </summary>
    Private Sub RebuildLanguageItems()
        Dim SelectedCode As String = TryGetSelectedCode(ComboLanguage)
        If String.IsNullOrEmpty(SelectedCode) Then SelectedCode = ModLanguage.CurrentLanguage
        Dim Entries As List(Of RegionLanguageEntry) = LanguageEntries
        IsRebuildingItems = True
        Try
            ComboLanguage.Items.Clear()
            For Each Entry In Entries
                ComboLanguage.Items.Add(New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code})
            Next
            ComboLanguage.SelectedIndex = Math.Max(0, Entries.FindIndex(Function(e) e.Code = SelectedCode))
        Finally
            IsRebuildingItems = False
        End Try
    End Sub

    ''' <summary>
    ''' 刷新弹窗内的全部文本。标题与按钮由弹窗自行按当前语言填充，以便切换语言后立即更新。
    ''' </summary>
    Private Sub RefreshTexts()
        LabTitle.Text = If(String.IsNullOrEmpty(MyConverter.Title), T("区域与语言"), MyConverter.Title)
        Btn1.Text = If(String.IsNullOrEmpty(MyConverter.Button1), T("确定"), MyConverter.Button1)
        LabRegion.Text = T("区域")
        LabLanguage.Text = T("语言")
        '每一段都是完整句子，用换行连接
        LabHint.Text = T("区域决定部分功能的可用性限制。") & vbCrLf &
                       T("更改界面语言后需要重启启动器才能完全生效。")
        RefreshToolTips()
    End Sub

    ''' <summary>
    ''' 刷新下拉框的提示文本。ToolTip 通过 I18n 扩展设置时不会随语言切换更新，因此这里按当前语言重设。
    ''' </summary>
    Private Sub RefreshToolTips()
        '与设置页使用同一条完整提示，避免同一句文案出现多份键
        ToolTipService.SetToolTip(ComboRegion, T("不同区域可能对应不同的功能限制。" & vbCrLf &
                                                 "亚洲（中国大陆）：不提供 Minecraft 下载与安装功能，但仍会显示入口并给出提示。" & vbCrLf &
                                                 "亚洲（其他）、非洲、美洲、欧洲、大洋洲：必须先登录微软账号，才能使用离线账户。"))
        ToolTipService.SetToolTip(ComboLanguage, T("选择启动器界面使用的语言。" & vbCrLf & "更改后需要重启启动器才能生效。"))
    End Sub

    ''' <summary>
    ''' 在弹窗内切换语言时立即切换界面文本，便于确认语言效果。
    ''' </summary>
    Private Sub ComboLanguage_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If IsRebuildingItems OrElse Not IsLoaded Then Return
        Try
            Dim Code As String = TryGetSelectedCode(ComboLanguage)
            If String.IsNullOrEmpty(Code) OrElse Code = ModLanguage.CurrentLanguage Then Return
            ModLanguage.LoadLanguage(Code)
            '区域名称是翻译文本，需要按新语言重建；语言下拉框的名称来自语言清单，不受影响
            RebuildRegionItems()
            RefreshTexts()
            Logger.Info($"区域与语言弹窗：预览界面语言 {Code}")
        Catch ex As Exception
            Logger.Error(ex, "预览界面语言失败", LogBehavior.Toast)
        End Try
    End Sub

    Private Sub Load(sender As Object, e As EventArgs) Handles MyBase.Loaded
        Try

            'UI 初始化
            Btn1.ColorType = MyButton.ColorState.Highlight
            '动画
            Opacity = 0
            AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, If(MyConverter.IsWarn, New MyColor(140, 80, 0, 0), New MyColor(90, 0, 0, 0)) - FrmMain.PanMsg.Background, 200), "PanMsg Background")
            AniStart({
                AaOpacity(Me, 1, 120, 60),
                AaDouble(Sub(i) TransformPos.Y += i, -TransformPos.Y, 300, 60, New AniEaseOutBack(AniEasePower.Weak)),
                AaDouble(Sub(i) TransformRotate.Angle += i, -TransformRotate.Angle, 300, 60, New AniEaseOutFluent(AniEasePower.Weak))
            }, "MyMsgBox " & Uuid)
            '记录日志
            Logger.Info($"区域与语言弹窗：{LabTitle.Text}")

        Catch ex As Exception
            Logger.Error(ex, "区域与语言弹窗加载失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub Close()
        '结束线程阻塞
        MyConverter.WaitFrame.Continue = False
        Interop.ComponentDispatcher.PopModal()
        '动画
        AniStart({
            AaCode(
            Sub()
                If Not WaitingMyMsgBox.Any() Then
                    AniStart(AaColor(FrmMain.PanMsg, Grid.BackgroundProperty, New MyColor(0, 0, 0, 0) - FrmMain.PanMsg.Background, 200, Ease:=New AniEaseOutFluent(AniEasePower.Weak)))
                End If
            End Sub, 30),
            AaOpacity(Me, -Opacity, 80, 20),
            AaDouble(Sub(i) TransformPos.Y += i, 20 - TransformPos.Y, 150, 0, New AniEaseOutFluent),
            AaDouble(Sub(i) TransformRotate.Angle += i, 6 - TransformRotate.Angle, 150, 0, New AniEaseInFluent(AniEasePower.Weak)),
            AaCode(Sub() CType(Parent, Grid).Children.Remove(Me), , True)
        }, "MyMsgBox " & Uuid)
    End Sub

    Public Sub Btn1_Click() Handles Btn1.Click
        If MyConverter.IsExited Then Return
        MyConverter.IsExited = True
        Dim SelectedLanguage As String = GetSelectedCode(ComboLanguage, LanguageEntries)
        '确保选择的语言已经应用，随后的重启提醒会使用新语言
        If SelectedLanguage <> ModLanguage.CurrentLanguage Then
            Try
                ModLanguage.LoadLanguage(SelectedLanguage)
                RefreshTexts()
            Catch ex As Exception
                Logger.Error(ex, "应用选择的界面语言失败", LogBehavior.Toast)
            End Try
        End If
        MyConverter.Result = New RegionLanguageSelection With {
            .Region = GetSelectedCode(ComboRegion, RegionEntries),
            .Language = SelectedLanguage
        }
        Close()
    End Sub

    Private Function TryGetSelectedCode(Combo As MyComboBox) As String
        Dim Item = TryCast(Combo.SelectedItem, MyComboBoxItem)
        If Item IsNot Nothing AndAlso Item.Tag IsNot Nothing Then Return Item.Tag.ToString()
        Return Nothing
    End Function

    Private Function GetSelectedCode(Combo As MyComboBox, Entries As List(Of RegionLanguageEntry)) As String
        Dim Code As String = TryGetSelectedCode(Combo)
        If Not String.IsNullOrEmpty(Code) Then Return Code
        Return Entries(0).Code
    End Function

    Private Sub Drag(sender As Object, e As MouseButtonEventArgs) Handles PanBorder.MouseLeftButtonDown, LabTitle.MouseLeftButtonDown
        On Error Resume Next
        If e.GetPosition(ShapeLine).Y <= 2 Then FrmMain.DragMove()
    End Sub

End Class
