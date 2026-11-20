Imports System.Globalization
Imports System.Reflection
Imports System.Windows.Markup

''' <summary>
''' 启动器多语言支持。
''' 语言包使用 JSON，键为基础语言（zh_cn）的完整原文，值为对应语言的文本。
''' 基础语言包必须完整；其它语言允许缺键，缺键时逐键回退到基础语言包。
''' 切换语言后需要重启启动器才能完全生效。
''' </summary>
Public Module ModLanguage

    ''' <summary>
    ''' 默认语言（基础语言）代码。
    ''' </summary>
    Public Const DefaultLanguage As String = "zh_cn"

    ''' <summary>
    ''' 语言包内嵌资源名称的固定部分。
    ''' </summary>
    Private Const ResourceMarker As String = ".Languages."

    ''' <summary>
    ''' 语言清单文件的固定名称。
    ''' </summary>
    Private Const ManifestFileName As String = "manifest.json"

    Private LanguageData As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private MissingKeys As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly LanguageDisplayNames As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly AvailableLanguageCache As New List(Of String)
    Private LanguageDictionary As ResourceDictionary = Nothing
    Private _CurrentLanguage As String = DefaultLanguage
    Private _IsInitialized As Boolean = False
    Private _IsLanguageMetadataApplied As Boolean = False

    ''' <summary>
    ''' 当前启动器语言代码，例如 zh_cn。
    ''' </summary>
    Public ReadOnly Property CurrentLanguage As String
        Get
            Return _CurrentLanguage
        End Get
    End Property

    ''' <summary>
    ''' 当前是否使用默认语言（简体中文）。
    ''' 部分来自外部的单语言资源仅对简体中文用户可见。
    ''' </summary>
    Public ReadOnly Property IsDefaultLanguage As Boolean
        Get
            Return CurrentLanguage = DefaultLanguage
        End Get
    End Property

    ''' <summary>
    ''' 是否已经加载过语言包。
    ''' </summary>
    Public ReadOnly Property IsInitialized As Boolean
        Get
            Return _IsInitialized
        End Get
    End Property

    ''' <summary>
    ''' 当前可用的语言代码列表。
    ''' </summary>
    Public ReadOnly Property AvailableLanguages As IReadOnlyList(Of String)
        Get
            Return AvailableLanguageCache
        End Get
    End Property

    ''' <summary>
    ''' 初始化语言包。应在创建任何窗口或页面之前调用。
    ''' 若配置文件中还没有语言设置（例如首次启动），会尝试跟随系统语言。
    ''' </summary>
    Public Sub Initialize()
        If _IsInitialized Then Return
        Dim SavedLanguage As String = ""
        Try
            '直接读取配置文件，避免在语言包加载前触发 Settings 的静态初始化
            SavedLanguage = ReadIni("Setup", "SystemLanguage")
        Catch ex As Exception
            '配置文件尚未就绪时忽略，按首次启动处理
        End Try

        Dim Language As String
        If String.IsNullOrWhiteSpace(SavedLanguage) Then
            '首次启动：检查系统语言是否受支持，支持则自动切换
            Language = DetectSystemLanguage()
            Logger.Info($"未找到语言设置，已根据系统语言自动选择：{Language}")
        Else
            Language = NormalizeLanguage(SavedLanguage)
        End If
        LoadLanguage(Language)
        _IsInitialized = True
    End Sub

    ''' <summary>
    ''' 检查系统语言是否受支持。若受支持，返回对应的语言代码；否则返回默认语言。
    ''' 依次尝试当前界面语言、系统安装语言与系统区域设置，并按语言代码前缀回退（例如 en_us 找不到时使用任意 en_*）。
    ''' </summary>
    Public Function DetectSystemLanguage() As String
        Try
            RefreshAvailableLanguages()
            For Each Candidate In EnumerateSystemLanguageCandidates()
                Dim Match = FindSupportedLanguage(Candidate)
                If Match IsNot Nothing Then Return Match
            Next
        Catch ex As Exception
            Logger.Warn(ex, "检查系统语言失败")
        End Try
        Return DefaultLanguage
    End Function

    ''' <summary>
    ''' 在可用语言中查找与指定语言代码匹配的语言。
    ''' 优先精确匹配，其次匹配同语言的其他区域（例如 zh_tw → zh_cn）。
    ''' 若没有匹配项，返回 Nothing。
    ''' </summary>
    Public Function FindSupportedLanguage(Language As String) As String
        If String.IsNullOrWhiteSpace(Language) Then Return Nothing
        Dim Code As String = NormalizeLanguage(Language)
        If AvailableLanguageCache.Contains(Code) Then Return Code
        Dim Neutral As String = Code.Split("_"c)(0)
        Return AvailableLanguageCache.FirstOrDefault(
            Function(Available) Available = Neutral OrElse Available.StartsWith(Neutral & "_", StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' 按优先级枚举可用于匹配系统语言的候选代码。
    ''' </summary>
    Private Iterator Function EnumerateSystemLanguageCandidates() As IEnumerable(Of String)
        Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each Culture As CultureInfo In {CultureInfo.CurrentUICulture, CultureInfo.InstalledUICulture, CultureInfo.CurrentCulture}
            If Culture Is Nothing Then Continue For
            For Each Candidate In GetCultureLanguageCodes(Culture)
                If Not String.IsNullOrEmpty(Candidate) AndAlso Seen.Add(Candidate) Then Yield Candidate
                '同时尝试仅含语言部分的代码，以便匹配任意同语言的语言包
                Dim Neutral As String = Candidate.Split("_"c)(0)
                If Seen.Add(Neutral) Then Yield Neutral
            Next
        Next
    End Function

    ''' <summary>
    ''' 将区域信息转换为语言代码，例如 zh-Hans-CN → zh_cn、en-US → en_us。
    ''' </summary>
    Private Function GetCultureLanguageCodes(Culture As CultureInfo) As IEnumerable(Of String)
        Dim Result As New List(Of String)
        If String.IsNullOrEmpty(Culture.Name) Then Return Result
        Dim Parts As String() = Culture.Name.Split("-"c)
        If Parts.Length = 1 Then
            Result.Add(Parts(0).ToLowerInvariant())
        Else
            '语言部分取第一段，区域部分取最后一段，跳过 Hans、Hant 等书写系统子标签
            Result.Add((Parts(0) & "_" & Parts(Parts.Length - 1)).ToLowerInvariant())
        End If
        If Culture.Parent IsNot Nothing AndAlso Not String.IsNullOrEmpty(Culture.Parent.Name) Then
            Result.Add(Culture.Parent.Name.Split("-"c)(0).ToLowerInvariant())
        End If
        Return Result
    End Function

    ''' <summary>
    ''' 语言切换完成后触发，参数为新的语言代码。
    ''' 用于刷新直接赋值而非通过 DynamicResource 绑定的文本（例如下拉框选项）。
    ''' </summary>
    Public Event LanguageChanged As Action(Of String)

    ''' <summary>
    ''' 重新加载指定语言。切换语言后仍建议重启启动器，以刷新代码中直接赋值的文本。
    ''' </summary>
    Public Sub LoadLanguage(Language As String)
        Language = NormalizeLanguage(Language)
        RefreshAvailableLanguages()
        If Not AvailableLanguageCache.Contains(Language) Then Language = DefaultLanguage

        '先在临时字典中组装，再整体替换，避免后台线程调用 T() 时读到半成品
        Dim NewData As New Dictionary(Of String, String)(StringComparer.Ordinal)

        '先加载基础语言，再叠加目标语言，实现缺键回退
        LoadEmbeddedLanguage(NewData, DefaultLanguage)
        LoadExternalLanguage(NewData, DefaultLanguage)
        If Language <> DefaultLanguage Then
            LoadEmbeddedLanguage(NewData, Language)
            LoadExternalLanguage(NewData, Language)
        End If

        If NewData.Count = 0 Then
            Logger.Error($"基础语言包 {DefaultLanguage}.json 未能加载")
        End If

        LanguageData = NewData
        MissingKeys = New HashSet(Of String)(StringComparer.Ordinal)
        _CurrentLanguage = Language
        ApplyCulture(Language)
        ApplyResourceDictionary()
        RaiseEvent LanguageChanged(Language)
    End Sub

    ''' <summary>
    ''' 获取语言文本。缺键时返回键本身（也就是基础语言的原文），并记录一次日志。
    ''' </summary>
    Public Function T(Key As String, ParamArray Args As Object()) As String
        If String.IsNullOrEmpty(Key) Then Return ""
        Key = NormalizeKey(Key)
        Dim Value As String = Nothing
        If Not LanguageData.TryGetValue(Key, Value) Then
            If MissingKeys.Add(Key) Then Logger.Warn($"缺少语言键：{Key}")
            Return Key
        End If
        If Args Is Nothing OrElse Args.Length = 0 Then Return Value
        Try
            Return String.Format(Value, Args)
        Catch ex As FormatException
            Logger.Warn(ex, $"语言键格式错误：{Key}")
            Return Value
        End Try
    End Function

    ''' <summary>
    ''' 检查语言键是否存在。
    ''' </summary>
    Public Function HasKey(Key As String) As Boolean
        Return Not String.IsNullOrEmpty(Key) AndAlso LanguageData.ContainsKey(NormalizeKey(Key))
    End Function

    ''' <summary>
    ''' 统一键中的换行符，避免 vbCrLf 与 JSON 中的 \n 不匹配。
    ''' </summary>
    Private Function NormalizeKey(Key As String) As String
        If String.IsNullOrEmpty(Key) Then Return ""
        Return Key.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
    End Function

    ''' <summary>
    ''' 获取语言在界面上的显示名称。显示名来自语言清单，避免翻译后语言名互相看不懂。
    ''' </summary>
    Public Function GetDisplayName(Code As String) As String
        Code = NormalizeLanguage(Code)
        Dim Name As String = Nothing
        If LanguageDisplayNames.TryGetValue(Code, Name) AndAlso Not String.IsNullOrWhiteSpace(Name) Then Return Name
        Return Code
    End Function

    ''' <summary>
    ''' 规范化语言代码，例如将 zh-CN 转换为 zh_cn。
    ''' </summary>
    Public Function NormalizeLanguage(Language As String) As String
        If String.IsNullOrWhiteSpace(Language) Then Return DefaultLanguage
        Return Language.Trim().Replace("-", "_").ToLowerInvariant()
    End Function

    ''' <summary>
    ''' 将语言代码转换为 .NET 区域代码，例如 zh_cn -> zh-CN。
    ''' </summary>
    Public Function ToCultureName(Language As String) As String
        Language = NormalizeLanguage(Language)
        If Language.Contains("_") Then
            Dim Parts = Language.Split("_"c)
            Return Parts(0) & "-" & Parts(1).ToUpperInvariant()
        End If
        Return Language
    End Function

    Private Sub LoadEmbeddedLanguage(Target As Dictionary(Of String, String), Language As String)
        Dim Text As String = ReadEmbeddedLanguage(Language)
        If String.IsNullOrWhiteSpace(Text) Then
            Logger.Warn($"内嵌语言文件不存在：{Language}")
            Return
        End If
        MergeLanguageJson(Target, Text, Language)
    End Sub

    Private Sub LoadExternalLanguage(Target As Dictionary(Of String, String), Language As String)
        Try
            Dim ExternalPath As String = Paths.Base & "FXL\Languages\" & Language & ".json"
            If Not FileUtils.Exists(ExternalPath) Then Return
            Dim ExternalText As String = FileUtils.TryReadAsString(ExternalPath, Encoding.UTF8)
            If Not String.IsNullOrWhiteSpace(ExternalText) Then MergeLanguageJson(Target, ExternalText, Language)
        Catch ex As Exception
            Logger.Warn(ex, $"读取外部语言文件失败：{Language}")
        End Try
    End Sub

    ''' <summary>
    ''' 将 JSON 逐键合并到目标语言数据中。外部语言包只覆盖同名的键，不做整体替换。
    ''' </summary>
    Private Sub MergeLanguageJson(Target As Dictionary(Of String, String), Text As String, Language As String)
        Try
            Dim Root As JObject = JObject.Parse(Text)
            For Each Item As JProperty In Root.Properties()
                If Item.Value Is Nothing Then Continue For
                Target(NormalizeKey(Item.Name)) = Item.Value.ToString()
            Next
        Catch ex As Exception
            Logger.Error(ex, $"语言文件解析失败：{Language}")
        End Try
    End Sub

    Private Function ReadEmbeddedLanguage(Language As String) As String
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            Dim ResourceName As String = Assembly.GetManifestResourceNames().
                FirstOrDefault(Function(Name) Name.EndsWith(ResourceMarker & Language & ".json", StringComparison.OrdinalIgnoreCase) OrElse
                                                   Name.EndsWith("." & Language & ".json", StringComparison.OrdinalIgnoreCase))
            If ResourceName Is Nothing Then Return ""
            Using Stream = Assembly.GetManifestResourceStream(ResourceName)
                If Stream Is Nothing Then Return ""
                Using Reader As New IO.StreamReader(Stream, Encoding.UTF8, True)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Error(ex, $"读取内嵌语言文件失败：{Language}")
            Return ""
        End Try
    End Function

    Private Sub RefreshAvailableLanguages()
        AvailableLanguageCache.Clear()
        LanguageDisplayNames.Clear()

        '读取语言清单（外部清单优先，便于用户添加语言）
        LoadManifest(ReadEmbeddedManifest())
        LoadManifest(ReadExternalManifest())

        '扫描内嵌语言包
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            For Each ResourceName In Assembly.GetManifestResourceNames()
                If Not ResourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim NoExtension As String = ResourceName.Substring(0, ResourceName.Length - 5)
                Dim Index As Integer = NoExtension.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase)
                If Index < 0 Then Continue For
                AddAvailableLanguage(NoExtension.Substring(Index + ResourceMarker.Length))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描内嵌语言包失败")
        End Try

        '扫描用户语言包
        Try
            Dim Folder As String = Paths.Base & "FXL\Languages\"
            If DirectoryUtils.Exists(Folder) Then
                For Each File In DirectoryUtils.EnumerateFiles(Folder, False, "*.json")
                    Dim Language As String = Path.GetFileNameWithoutExtension(File).ToLowerInvariant()
                    If Language <> "manifest" Then AddAvailableLanguage(Language)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "扫描外部语言包失败")
        End Try

        '基础语言必须始终可用，且排在第一位
        AvailableLanguageCache.RemoveAll(Function(Code) Code = DefaultLanguage)
        AvailableLanguageCache.Insert(0, DefaultLanguage)
        If Not LanguageDisplayNames.ContainsKey(DefaultLanguage) Then
            LanguageDisplayNames(DefaultLanguage) = DefaultLanguage
        End If
    End Sub

    Private Sub AddAvailableLanguage(Language As String)
        Language = NormalizeLanguage(Language)
        If String.IsNullOrWhiteSpace(Language) OrElse Language = "manifest" Then Return
        If Not AvailableLanguageCache.Contains(Language) Then AvailableLanguageCache.Add(Language)
    End Sub

    Private Function ReadEmbeddedManifest() As String
        Try
            Dim Assembly = GetType(ModLanguage).Assembly
            Dim ResourceName As String = Assembly.GetManifestResourceNames().
                FirstOrDefault(Function(Name) Name.EndsWith(ResourceMarker & ManifestFileName, StringComparison.OrdinalIgnoreCase))
            If ResourceName Is Nothing Then Return ""
            Using Stream = Assembly.GetManifestResourceStream(ResourceName)
                If Stream Is Nothing Then Return ""
                Using Reader As New IO.StreamReader(Stream, Encoding.UTF8, True)
                    Return Reader.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Logger.Warn(ex, "读取内嵌语言清单失败")
            Return ""
        End Try
    End Function

    Private Function ReadExternalManifest() As String
        Try
            Dim ExternalPath As String = Paths.Base & "FXL\Languages\" & ManifestFileName
            If FileUtils.Exists(ExternalPath) Then Return FileUtils.TryReadAsString(ExternalPath, Encoding.UTF8)
        Catch ex As Exception
            Logger.Warn(ex, "读取外部语言清单失败")
        End Try
        Return ""
    End Function

    Private Sub LoadManifest(Text As String)
        If String.IsNullOrWhiteSpace(Text) Then Return
        Try
            Dim Root As JObject = JObject.Parse(Text)
            Dim Languages = TryCast(Root("languages"), JArray)
            If Languages Is Nothing Then Return
            For Each Item As JObject In Languages.OfType(Of JObject)()
                Dim Code As String = NormalizeLanguage(Item("code")?.ToString())
                If String.IsNullOrWhiteSpace(Code) Then Continue For
                AddAvailableLanguage(Code)
                Dim Name As String = Item("name")?.ToString()
                If Not String.IsNullOrWhiteSpace(Name) Then LanguageDisplayNames(Code) = Name
            Next
        Catch ex As Exception
            Logger.Warn(ex, "解析语言清单失败")
        End Try
    End Sub

    Private Sub ApplyCulture(Language As String)
        Try
            Dim Culture As New CultureInfo(ToCultureName(Language))
            '只修改 UI 语言，不修改 CurrentCulture，避免影响日期、数字解析和排序
            CultureInfo.DefaultThreadCurrentUICulture = Culture
            CultureInfo.CurrentUICulture = Culture
            'Language 的元数据只能覆盖一次，且必须在创建任何控件之前完成
            If Not _IsLanguageMetadataApplied Then
                _IsLanguageMetadataApplied = True
                Dim XmlLang = XmlLanguage.GetLanguage(Culture.IetfLanguageTag)
                FrameworkElement.LanguageProperty.OverrideMetadata(GetType(FrameworkElement), New FrameworkPropertyMetadata(XmlLang))
            End If
        Catch ex As Exception
            Logger.Warn(ex, $"应用界面语言失败：{Language}")
        End Try
    End Sub

    Private Sub ApplyResourceDictionary()
        Try
            Dim App = Application.Current
            If App Is Nothing Then Return
            '资源字典属于 UI 线程，若在后台线程调用则切换过去更新
            If Not App.Dispatcher.CheckAccess() Then
                App.Dispatcher.Invoke(Sub() ApplyResourceDictionary())
                Return
            End If
            Dim NewDictionary As New ResourceDictionary()
            Dim Data As Dictionary(Of String, String) = LanguageData
            For Each Item In Data
                NewDictionary(Item.Key) = Item.Value
            Next
            '先加入新字典再移除旧字典，避免语言切换时短暂缺失文本
            App.Resources.MergedDictionaries.Add(NewDictionary)
            If LanguageDictionary IsNot Nothing Then App.Resources.MergedDictionaries.Remove(LanguageDictionary)
            LanguageDictionary = NewDictionary
        Catch ex As Exception
            Logger.Error(ex, "应用语言资源字典失败")
        End Try
    End Sub

End Module
