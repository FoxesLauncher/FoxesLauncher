Imports System.Text.RegularExpressions

''' <summary>
''' 匿名遥测：在本地收集事件，定期批量 POST 到 Cloudflare Worker。
''' 发送失败只写日志，不阻塞启动、更新或退出；Debug 构建与关闭开关时不发送。
''' </summary>
Friend Module ModTelemetry

    Private Class TelemetryEvent
        Public Name As String
        Public Time As DateTime
        Public Fields As JObject
    End Class

    Private ReadOnly Pending As New ConcurrentQueue(Of TelemetryEvent)
    Private ReadOnly StateLock As New Object
    Private FlusherStarted As Boolean
    Private FlushBusy As Integer
    Private LastFlushTick As Long = GetTimeMs()

    ''' <summary>
    ''' 记录一条遥测事件。保留原有 Telemetry(Event, ParamArray Datas) 调用方式。
    ''' Datas 为成对的键值：{"Source", "github", "Result", "success"}。
    ''' </summary>
    Public Sub Send([Event] As String, ParamArray Datas As String())
        Try
            If BuildType = BuildTypes.Debug Then Return '开发版不上传遥测
            If Not Settings.Get(Of Boolean)("SystemSystemTelemetry") Then Return '用户关闭了遥测
            If String.IsNullOrWhiteSpace(TelemetryUrl) Then Return '未配置遥测地址
            If String.IsNullOrWhiteSpace([Event]) Then Return

            Dim Fields As New JObject()
            For i = 0 To Datas.Length - 1 Step 2
                Dim Key = SanitizeKey(Datas(i))
                If String.IsNullOrEmpty(Key) Then Continue For
                Dim Value = If(i + 1 < Datas.Length, Datas(i + 1), "")
                Fields(Key) = SanitizeValue(Key, Value)
            Next

            Pending.Enqueue(New TelemetryEvent With {
                .Name = SanitizeKey([Event]),
                .Time = DateTime.UtcNow,
                .Fields = Fields
            })
            EnsureFlusher()
            If Pending.Count >= 40 Then
                RunInNewThread(Sub() Flush(), "Telemetry Flush", ThreadPriority.Lowest)
            End If
        Catch ex As Exception
            '遥测自身绝不能影响主流程，也不能递归上报
            Logger.Warn(ex, "记录匿名数据失败")
        End Try
    End Sub

    Private Sub EnsureFlusher()
        SyncLock StateLock
            If FlusherStarted Then Return
            FlusherStarted = True
        End SyncLock
        RunInNewThread(
        Sub()
            Try
                Do While True
                    Thread.Sleep(1000)
                    Dim Interval = Math.Max(5, TelemetryIntervalSeconds) * 1000
                    If Pending.Count >= 50 OrElse GetTimeMs() - LastFlushTick >= Interval Then Flush()
                Loop
            Catch ex As Exception
                Logger.Warn(ex, "遥测后台线程退出")
            End Try
        End Sub, "Telemetry Flusher", ThreadPriority.Lowest)
    End Sub

    ''' <summary>
    ''' 立即尝试发送当前队列。最多发送 50 条事件，剩余事件留待下次。
    ''' </summary>
    Private Sub Flush(Optional TimeoutMs As Integer = 10000)
        If Interlocked.CompareExchange(FlushBusy, 1, 0) <> 0 Then Return
        Try
            If String.IsNullOrWhiteSpace(TelemetryUrl) Then Return
            Dim Events As New JArray()
            Dim Item As TelemetryEvent = Nothing
            Do While Events.Count < 50 AndAlso Pending.TryDequeue(Item)
                Events.Add(New JObject From {
                    {"name", Item.Name},
                    {"ts", Item.Time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")},
                    {"fields", Item.Fields}
                })
            Loop
            If Events.Count = 0 Then Return

            Dim Body As New JObject From {
                {"schema", 1},
                {"installId", GetInstallId()},
                {"channel", Settings.Get(Of String)("UpdateChannel")},
                {"versionCode", VersionCode},
                {"versionName", VersionBaseName},
                {"os", GetOsDisplay()},
                {"region", CurrentRegion},
                {"events", Events}
            }

            Try
                NetRequestByClient(TelemetryUrl, HttpMethod.Post, Body.ToString(Newtonsoft.Json.Formatting.None),
                    ContentType:="application/json", Accept:="application/json", Timeout:=TimeoutMs, MakeLog:=False)
                Logger.Info($"匿名数据上报成功，共 {Events.Count} 条")
            Catch ex As Exception
                '失败时不重试，避免在离线环境堆积线程或请求。
                Logger.Warn(ex, "匿名数据上报失败，本批事件已丢弃")
            End Try
        Catch ex As Exception
            Logger.Warn(ex, "匿名数据上报异常")
        Finally
            LastFlushTick = GetTimeMs()
            Interlocked.Exchange(FlushBusy, 0)
        End Try
    End Sub

    ''' <summary>
    ''' 在程序退出前尽力发送一次，最多等待 maxWaitMs 毫秒。
    ''' </summary>
    Public Sub FlushNow(Optional MaxWaitMs As Integer = 2500)
        Try
            If Pending.IsEmpty Then Return
            Dim Worker As Thread = RunInNewThread(Sub() Flush(3000), "Telemetry Final Flush", ThreadPriority.Lowest)
            Worker.Join(MaxWaitMs)
        Catch ex As Exception
            Logger.Warn(ex, "退出前发送匿名数据失败")
        End Try
    End Sub

    ''' <summary>
    ''' 遥测用的匿名安装标识，与参与设置加密的 Identify 分离。
    ''' </summary>
    Private Function GetInstallId() As String
        Try
            Dim Value = Settings.Get(Of String)("SystemInstallId")
            If Not String.IsNullOrWhiteSpace(Value) Then Return Value
            Value = Guid.NewGuid().ToString("N")
            Settings.Set("SystemInstallId", Value)
            Return Value
        Catch ex As Exception
            Logger.Warn(ex, "生成匿名安装标识失败")
            Return "unknown"
        End Try
    End Function

    Private Function GetOsDisplay() As String
        Try
            Dim Result = My.Computer.Info.OSFullName
            If String.IsNullOrWhiteSpace(Result) Then Result = Environment.OSVersion.ToString()
            Return Limit(Result, 64)
        Catch
            Return Limit(Environment.OSVersion.ToString(), 64)
        End Try
    End Function

    Private Function SanitizeKey(Value As String) As String
        If String.IsNullOrWhiteSpace(Value) Then Return ""
        Dim Result = Regex.Replace(Value.Trim(), "[^A-Za-z0-9_.-]", "")
        Return Limit(Result, 48)
    End Function

    ''' <summary>
    ''' 客户端侧先做一次粗脱敏；服务端仍会再次过滤和截断。
    ''' </summary>
    Private Function SanitizeValue(Key As String, Value As String) As String
        If Value Is Nothing Then Return ""
        Dim Result = Value.Replace(vbCrLf, " ").Replace(vbCr, " ").Replace(vbLf, " ")
        Try
            '去除常见用户目录、临时目录中的用户名
            Result = Regex.Replace(Result, "([A-Za-z]:\\Users\\)[^\\\s]+", "$1<user>", RegexOptions.IgnoreCase)
            Result = Regex.Replace(Result, "([A-Za-z]:\\Documents and Settings\\)[^\\\s]+", "$1<user>", RegexOptions.IgnoreCase)
        Catch
        End Try
        Select Case Key
            Case "Exception"
                Return Limit(Result, 1024)
            Case "ExceptionCategory"
                Return Limit(Result, 64)
            Case Else
                Return Limit(Result, 256)
        End Select
    End Function

    Private Function Limit(Value As String, MaxLength As Integer) As String
        If Value Is Nothing Then Return ""
        If Value.Length <= MaxLength Then Return Value
        Return Value.Substring(0, MaxLength)
    End Function

End Module
