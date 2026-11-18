Public Class MyMsgRegionLanguage

    Private ReadOnly MyConverter As MyMsgBoxConverter
    Private ReadOnly Uuid As Integer = GetUuid()

    Public Sub New(Converter As MyMsgBoxConverter)
        Try

            InitializeComponent()
            Btn1.Name = Btn1.Name & GetUuid()
            MyConverter = Converter
            LabTitle.Text = Converter.Title
            Btn1.Text = Converter.Button1
            If Converter.IsWarn Then
                Btn1.ColorType = MyButton.ColorState.Red
                LabTitle.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushRedLight")
            End If
            ShapeLine.StrokeThickness = GetWPFSize(1)
            '添加区域与语言选项
            For Each Entry In RegionEntries
                ComboRegion.Items.Add(New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code})
            Next
            ComboRegion.SelectedIndex = Math.Max(0, RegionEntries.FindIndex(Function(e) e.Code = CurrentRegion))
            For Each Entry In LanguageEntries
                ComboLanguage.Items.Add(New MyComboBoxItem With {.Content = Entry.Name, .Tag = Entry.Code})
            Next
            ComboLanguage.SelectedIndex = Math.Max(0, LanguageEntries.FindIndex(Function(e) e.Code = CurrentLanguage))

        Catch ex As Exception
            Logger.Error(ex, "区域与语言弹窗初始化失败", LogBehavior.Toast)
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
        MyConverter.Result = New RegionLanguageSelection With {
            .Region = GetSelectedCode(ComboRegion, RegionEntries),
            .Language = GetSelectedCode(ComboLanguage, LanguageEntries)
        }
        Close()
    End Sub

    Private Function GetSelectedCode(Combo As MyComboBox, Entries As List(Of RegionLanguageEntry)) As String
        Dim Item = TryCast(Combo.SelectedItem, MyComboBoxItem)
        If Item IsNot Nothing AndAlso Item.Tag IsNot Nothing Then Return Item.Tag.ToString()
        Return Entries(0).Code
    End Function

    Private Sub Drag(sender As Object, e As MouseButtonEventArgs) Handles PanBorder.MouseLeftButtonDown, LabTitle.MouseLeftButtonDown
        On Error Resume Next
        If e.GetPosition(ShapeLine).Y <= 2 Then FrmMain.DragMove()
    End Sub

End Class
