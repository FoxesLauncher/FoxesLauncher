Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        ItemAboutFxl.Info = ItemAboutFxl.Info.Replace("%VERSION%", VersionDisplay).Replace("%VERSIONCODE%", VersionCode).Replace("%BRANCH%", CInt(BuildType))
        ItemAboutPcl.Info = T("上游版本：") & "2.99.0.1"
        '作者名称是专有名词，固定使用原文，不参与翻译
        ItemAuthorLTCat.Title = "龙腾猫跃"
        ItemAuthorLTCatPcl.Title = "龙腾猫跃"
        ItemAuthorHimic.Title = "皓辰不是猫"

    End Sub

End Class
