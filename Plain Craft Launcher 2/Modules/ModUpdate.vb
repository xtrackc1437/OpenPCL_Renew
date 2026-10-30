Imports System.Text.RegularExpressions

''' <summary>
''' 启动器更新。
''' 从 GitHub 仓库的 Releases 与 GitHub Actions 的构建记录中检测新版本，并支持自动下载与替换。
''' </summary>
Public Module ModUpdate

#Region "常量与模型"

    '更新源仓库
    Public Const UpdateRepoOwner As String = "xtrackc1437"
    Public Const UpdateRepoName As String = "OpenPCL_Renew"
    Public Const UpdateApiBase As String = "https://api.github.com/repos/" & UpdateRepoOwner & "/" & UpdateRepoName
    Public Const UpdateReleasesPage As String = "https://github.com/" & UpdateRepoOwner & "/" & UpdateRepoName & "/releases"
    '自动构建版的检测来源
    Public Const UpdateWorkflowFile As String = "build.yml"
    Public Const UpdateWorkflowName As String = "build"
    Public Const UpdateWorkflowBranch As String = "main"
    '自动构建版产物的预发布 tag
    Public Const UpdateNightlyTag As String = "nightly"

    ''' <summary>
    ''' 更新通道。
    ''' </summary>
    Public Enum UpdateChannel
        ''' <summary>
        ''' 正式版：Releases 中未标记为预发布的最新稳定版本。
        ''' </summary>
        Release = 0
        ''' <summary>
        ''' 测试版：Releases 中最新的版本，包含预发布。
        ''' </summary>
        Beta = 1
        ''' <summary>
        ''' 自动构建版：GitHub Actions 中 Build 工作流最近一次成功构建。
        ''' </summary>
        Build = 2
    End Enum

    ''' <summary>
    ''' 一次更新检测的结果。
    ''' </summary>
    Public Class UpdateInfo
        Public Channel As UpdateChannel
        ''' <summary>
        ''' 相比当前运行的版本是否有更新。
        ''' </summary>
        Public IsNewer As Boolean
        Public Version As Version
        ''' <summary>
        ''' 显示用的版本名，例如 2.99.1.0 或 构建 #10。
        ''' </summary>
        Public VersionText As String = ""
        Public Title As String = ""
        Public Changelog As String = ""
        Public PublishTime As Date?
        ''' <summary>
        ''' 在浏览器中查看该版本的页面。
        ''' </summary>
        Public PageUrl As String = UpdateReleasesPage
        ''' <summary>
        ''' 可直接下载的地址。为空代表需要手动下载。
        ''' </summary>
        Public DownloadUrl As String = ""
        ''' <summary>
        ''' 自动构建版对应的 Commit Hash。
        ''' </summary>
        Public Sha As String = ""
        Public RunNumber As Integer
    End Class

    ''' <summary>
    ''' 更新文件的暂存文件夹，以 \ 结尾。
    ''' 放在启动器同级的 PCL 文件夹下，避免临时目录被系统清理。
    ''' </summary>
    Public ReadOnly Property UpdateStageFolder As String
        Get
            Return Paths.Base & "PCL\Update\"
        End Get
    End Property

#End Region

#Region "设置与缓存"

    ''' <summary>
    ''' 当前设置的更新通道。
    ''' </summary>
    Public Function GetUpdateChannelSetting() As UpdateChannel
        Return CType(Settings.Get(Of Integer)("ToolUpdateChannel"), UpdateChannel)
    End Function

    Private Function NowMs() As Long
        Return Date.Now.Ticks \ TimeSpan.TicksPerMillisecond
    End Function

    Private Function GetUpdateCache() As JObject
        Try
            Dim Raw As String = Settings.Get(Of String)("CacheUpdateResult")
            If String.IsNullOrWhiteSpace(Raw) Then Return New JObject
            Return TryCast(JToken.Parse(Raw), JObject)
        Catch ex As Exception
            Return New JObject
        End Try
    End Function

    Private Sub SaveUpdateCache(Info As UpdateInfo)
        Try
            Dim Cache = GetUpdateCache()
            Dim Node As New JObject
            Node("Time") = NowMs()
            Node("IsNewer") = Info.IsNewer
            Node("Version") = If(Info.Version Is Nothing, "", Info.Version.ToString)
            Node("VersionText") = Info.VersionText
            Node("Title") = Info.Title
            Node("Changelog") = Info.Changelog
            Node("PublishTime") = If(Info.PublishTime.HasValue, Info.PublishTime.Value.ToString("o"), "")
            Node("PageUrl") = Info.PageUrl
            Node("DownloadUrl") = Info.DownloadUrl
            Node("Sha") = Info.Sha
            Node("RunNumber") = Info.RunNumber
            Cache(CInt(Info.Channel).ToString) = Node
            Settings.Set("CacheUpdateResult", Cache.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            Logger.Warn(ex, "保存更新缓存失败")
        End Try
    End Sub

    ''' <summary>
    ''' 获取指定通道上次检测的缓存结果。若没有缓存则返回 Nothing。
    ''' </summary>
    Public Function GetCachedUpdate(Channel As UpdateChannel) As UpdateInfo
        Try
            Dim Node = GetUpdateCache()(CInt(Channel).ToString)
            If Node Is Nothing Then Return Nothing
            Dim Result As New UpdateInfo With {.Channel = Channel}
            Result.IsNewer = If(Node("IsNewer")?.ToObject(Of Boolean)(), False)
            Result.VersionText = If(Node("VersionText")?.ToString, "")
            Result.Title = If(Node("Title")?.ToString, "")
            Result.Changelog = If(Node("Changelog")?.ToString, "")
            Result.PageUrl = If(Node("PageUrl")?.ToString, UpdateReleasesPage)
            Result.DownloadUrl = If(Node("DownloadUrl")?.ToString, "")
            Result.Sha = If(Node("Sha")?.ToString, "")
            Result.RunNumber = If(Node("RunNumber")?.ToObject(Of Integer)(), 0)
            Dim VersionText = Node("Version")?.ToString
            If Not String.IsNullOrWhiteSpace(VersionText) Then
                Dim Parsed As Version = Nothing
                If Version.TryParse(VersionText, Parsed) Then Result.Version = Parsed
            End If
            Dim PublishText = Node("PublishTime")?.ToString
            If Not String.IsNullOrWhiteSpace(PublishText) Then
                Dim ParsedTime As Date
                If Date.TryParse(PublishText, ParsedTime) Then Result.PublishTime = ParsedTime
            End If
            Return Result
        Catch ex As Exception
            Logger.Warn(ex, "读取更新缓存失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 获取指定通道上次检测的时间戳（毫秒）。若没有记录则返回 0。
    ''' </summary>
    Public Function GetUpdateLastCheckTime(Channel As UpdateChannel) As Long
        Try
            Dim Node = GetUpdateCache()(CInt(Channel).ToString)
            If Node Is Nothing Then Return 0
            Return If(Node("Time")?.ToObject(Of Long)(), 0L)
        Catch ex As Exception
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 根据「自动检查」设置判断当前是否需要进行一次更新检测。
    ''' </summary>
    Public Function IsUpdateCheckDue() As Boolean
        Dim Mode = Settings.Get(Of Integer)("ToolUpdateCheck")
        If Mode = 3 Then Return False '从不自动检查
        If Mode = 0 Then Return True '每次启动时检查
        Dim LastCheck = GetUpdateLastCheckTime(GetUpdateChannelSetting())
        If LastCheck <= 0 Then Return True
        Dim Interval As Long = If(Mode = 1, 6L, 24L) * 60 * 60 * 1000
        Return NowMs() - LastCheck > Interval
    End Function

    ''' <summary>
    ''' 供崩溃分析等逻辑使用：当前是否已经是最新版本。返回 Nothing 代表没有可用的检测结果。
    ''' </summary>
    Public Function GetCachedIsNewest() As Boolean?
        Dim Info = GetCachedUpdate(GetUpdateChannelSetting())
        If Info Is Nothing Then Return Nothing
        Return Not Info.IsNewer
    End Function

#End Region

#Region "网络请求"

    Private Function UpdateApiGet(Url As String) As JToken
        Dim Raw As String
        Try
            Raw = NetRequestByClient(Url, Accept:="application/vnd.github+json", Timeout:=15000)
        Catch ex As Exception
            Throw New Exception("无法连接 GitHub，请检查网络后重试。" & vbCrLf & ex.Message, ex)
        End Try
        Dim Token As JToken
        Try
            Token = JToken.Parse(Raw)
        Catch ex As Exception
            Throw New Exception("GitHub 接口返回了无法解析的内容。", ex)
        End Try
        Dim ResultObject = TryCast(Token, JObject)
        If ResultObject IsNot Nothing Then
            Dim Message = ResultObject("message")?.ToString
            If Message IsNot Nothing AndAlso ResultObject("tag_name") Is Nothing AndAlso ResultObject("workflow_runs") Is Nothing Then
                If Message.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Throw New Exception("GitHub 接口访问次数已达上限，请稍后再试。")
                End If
                Throw New Exception("GitHub 接口返回错误：" & Message)
            End If
        End If
        Return Token
    End Function

    Private Function ParseVersion(Tag As String) As Version
        If String.IsNullOrWhiteSpace(Tag) Then Return Nothing
        Dim Match = Regex.Match(Tag, "\d+(?:\.\d+){0,3}")
        If Not Match.Success Then Return Nothing
        Dim Text = Match.Value
        If Not Text.Contains(".") Then Text &= ".0"
        Dim Result As Version = Nothing
        If Version.TryParse(Text, Result) Then Return Result
        Return Nothing
    End Function

    Private Function ParseTime(Node As JToken) As Date
        If Node Is Nothing Then Return Date.MinValue
        Try
            Return Date.Parse(Node.ToString, Globalization.CultureInfo.InvariantCulture,
                              Globalization.DateTimeStyles.AssumeUniversal Or Globalization.DateTimeStyles.AdjustToUniversal).ToLocalTime()
        Catch ex As Exception
            Return Date.MinValue
        End Try
    End Function

    ''' <summary>
    ''' 去掉 Markdown 标记并截断过长的更新说明。
    ''' </summary>
    Private Function FormatChangelog(Body As String) As String
        If String.IsNullOrWhiteSpace(Body) Then Return ""
        Dim Result = Body.Replace(vbCrLf, vbLf)
        Result = Regex.Replace(Result, "!\[[^\]]*\]\([^)]*\)", "")
        Result = Regex.Replace(Result, "\[([^\]]*)\]\([^)]*\)", "$1")
        Result = Regex.Replace(Result, "^#{1,6}\s*", "", RegexOptions.Multiline)
        Result = Regex.Replace(Result, "^>\s?", "", RegexOptions.Multiline)
        Result = Result.Replace("`", "").Replace("**", "")
        Result = Regex.Replace(Result, "\n{3,}", vbLf & vbLf)
        Result = Result.Replace(vbLf, vbCrLf).Trim()
        If Result.Length > 800 Then Result = Result.Substring(0, 800) & "……"
        Return Result
    End Function

#End Region

#Region "检测"

    ''' <summary>
    ''' 检测指定通道的最新可用版本，并写入缓存。失败会抛出异常。
    ''' </summary>
    Public Function UpdateCheck(Channel As UpdateChannel) As UpdateInfo
        Dim Result As UpdateInfo
        Select Case Channel
            Case UpdateChannel.Release
                Result = UpdateCheckRelease(False)
            Case UpdateChannel.Beta
                Result = UpdateCheckRelease(True)
            Case UpdateChannel.Build
                Result = UpdateCheckBuild()
            Case Else
                Throw New Exception("未知的更新通道：" & Channel)
        End Select
        If Result IsNot Nothing Then
            Result.Channel = Channel
            SaveUpdateCache(Result)
            Logger.Info($"更新检测完成（{Channel}）：{Result.VersionText}，是否为新版本：{Result.IsNewer}")
        End If
        Return Result
    End Function

    Private Function UpdateCheckRelease(IncludePrerelease As Boolean) As UpdateInfo
        Dim Token = UpdateApiGet(UpdateApiBase & "/releases?per_page=30")
        Dim Items = CType(Token, JArray).OfType(Of JObject)()
        Dim Best As JObject = Nothing
        Dim BestVersion As Version = Nothing
        Dim BestTime As Date = Date.MinValue
        For Each Item In Items
            If Item("draft")?.ToObject(Of Boolean)() Then Continue For
            If Not IncludePrerelease AndAlso Item("prerelease")?.ToObject(Of Boolean)() Then Continue For
            Dim Ver = ParseVersion(Item("tag_name")?.ToString)
            Dim ItemTime = ParseTime(Item("published_at"))
            If IncludePrerelease Then
                '测试版：取发布时间最新的版本
                If ItemTime > BestTime Then
                    Best = Item
                    BestTime = ItemTime
                    BestVersion = Ver
                End If
            Else
                '正式版：取版本号最大的版本
                If Ver IsNot Nothing AndAlso (BestVersion Is Nothing OrElse Ver > BestVersion) Then
                    Best = Item
                    BestVersion = Ver
                    BestTime = ItemTime
                End If
            End If
        Next
        If Best Is Nothing Then
            If IncludePrerelease Then
                Throw New Exception("该仓库中还没有发布任何版本。")
            Else
                Throw New Exception("该仓库中还没有发布正式版。" & vbCrLf & "你可以把更新通道切换为「测试版」或「自动构建版」。")
            End If
        End If

        Dim BestPublishTime As Date? = Nothing
        If BestTime <> Date.MinValue Then BestPublishTime = BestTime

        Dim Info As New UpdateInfo With {
            .Version = BestVersion,
            .VersionText = If(BestVersion Is Nothing, Best("tag_name")?.ToString, BestVersion.ToString),
            .Title = If(Best("name")?.ToString, Best("tag_name")?.ToString),
            .Changelog = FormatChangelog(Best("body")?.ToString),
            .PublishTime = BestPublishTime,
            .PageUrl = If(Best("html_url")?.ToString, UpdateReleasesPage)
        }
        If String.IsNullOrWhiteSpace(Info.Title) Then Info.Title = Info.VersionText

        Dim Assets = TryCast(Best("assets"), JArray)
        If Assets IsNot Nothing Then
            Dim ExeAsset = Assets.OfType(Of JObject)().FirstOrDefault(Function(a) If(a("name")?.ToString, "").EndsWithF(".exe", True))
            If ExeAsset IsNot Nothing Then Info.DownloadUrl = If(ExeAsset("browser_download_url")?.ToString, "")
        End If

        Info.IsNewer = IsVersionNewer(Info.Version)
        Return Info
    End Function

    Private Function IsVersionNewer(Remote As Version) As Boolean
        If Remote Is Nothing Then Return False
        Dim LocalVersion As Version = Nothing
        If Not Version.TryParse(VersionBaseName, LocalVersion) Then Return False
        Return Remote > LocalVersion
    End Function

    Private Function UpdateCheckBuild() As UpdateInfo
        Dim Url = $"{UpdateApiBase}/actions/workflows/{UpdateWorkflowFile}/runs?branch={UpdateWorkflowBranch}&status=success&per_page=1"
        Dim Token = UpdateApiGet(Url)
        Dim Runs = TryCast(CType(Token, JObject)("workflow_runs"), JArray)
        If Runs Is Nothing Then Throw New Exception("GitHub 接口没有返回构建记录。")
        Dim Run = Runs.OfType(Of JObject)().FirstOrDefault
        If Run Is Nothing Then
            Throw New Exception($"Build 工作流在 {UpdateWorkflowBranch} 分支上还没有成功的构建记录。")
        End If

        Dim Sha = If(Run("head_sha")?.ToString, "")
        Dim ShortSha = If(Sha.Length >= 7, Sha.Substring(0, 7), Sha)
        Dim RunNumber = If(Run("run_number")?.ToObject(Of Integer)(), 0)
        Dim RunId = If(Run("id")?.ToObject(Of Long)(), 0L)

        Dim ParsedBuildTime = ParseTime(Run("created_at"))
        Dim BuildTime As Date? = Nothing
        If ParsedBuildTime <> Date.MinValue Then BuildTime = ParsedBuildTime

        Dim Info As New UpdateInfo With {
            .VersionText = $"构建 #{RunNumber}",
            .Title = $"自动构建 #{RunNumber}（{ShortSha}）",
            .PublishTime = BuildTime,
            .PageUrl = If(Run("html_url")?.ToString, UpdateReleasesPage),
            .Sha = Sha,
            .RunNumber = RunNumber
        }
        Dim Text As New StringBuilder
        Text.AppendLine($"提交：{Sha}")
        If Info.PublishTime.HasValue Then Text.AppendLine($"构建时间：{Info.PublishTime.Value:yyyy-MM-dd HH:mm}")
        Text.Append("由 GitHub Actions 自动构建，可能不稳定。")
        Info.Changelog = Text.ToString

        '本地带有 Commit 信息时据其判断，否则只能认为存在更新
        Info.IsNewer = CommitHash = "" OrElse Not String.Equals(Sha, CommitHash, StringComparison.OrdinalIgnoreCase)
        Info.DownloadUrl = GetBuildDownloadUrl(RunId, ShortSha)
        Return Info
    End Function

    ''' <summary>
    ''' 获取自动构建版的下载地址。优先使用 nightly 预发布中的产物，失败时改用 nightly.link 转发。
    ''' </summary>
    Private Function GetBuildDownloadUrl(RunId As Long, ShortSha As String) As String
        If ShortSha <> "" Then
            Try
                Dim Token = UpdateApiGet($"{UpdateApiBase}/releases/tags/{UpdateNightlyTag}")
                Dim Assets = TryCast(CType(Token, JObject)("assets"), JArray)
                If Assets IsNot Nothing Then
                    Dim NightlyAsset = Assets.OfType(Of JObject)().FirstOrDefault(
                        Function(a) If(a("name")?.ToString, "").IndexOf(ShortSha, StringComparison.OrdinalIgnoreCase) >= 0)
                    If NightlyAsset IsNot Nothing Then Return If(NightlyAsset("browser_download_url")?.ToString, "")
                End If
            Catch ex As Exception
                Logger.Warn(ex, "获取自动构建产物失败，尝试改用 nightly.link")
            End Try
        End If
        If RunId <= 0 Then Return ""
        Try
            Dim Token = UpdateApiGet($"{UpdateApiBase}/actions/runs/{RunId}/artifacts")
            Dim Artifacts = TryCast(CType(Token, JObject)("artifacts"), JArray)
            If Artifacts IsNot Nothing Then
                Dim Items = Artifacts.OfType(Of JObject)().Where(Function(a) Not If(a("expired")?.ToObject(Of Boolean)(), False))
                Dim Asset = Items.FirstOrDefault(Function(a) If(a("name")?.ToString, "").StartsWithF("Release ", True))
                If Asset Is Nothing Then Asset = Items.FirstOrDefault(Function(a) If(a("name")?.ToString, "").StartsWithF("Debug ", True))
                If Asset IsNot Nothing Then
                    Dim ArtifactName = Asset("name").ToString
                    Return $"https://nightly.link/{UpdateRepoOwner}/{UpdateRepoName}/workflows/{UpdateWorkflowName}/{UpdateWorkflowBranch}/{Uri.EscapeDataString(ArtifactName)}.zip"
                End If
            End If
        Catch ex As Exception
            Logger.Warn(ex, "获取自动构建产物列表失败")
        End Try
        Return ""
    End Function

#End Region

#Region "下载与替换"

    ''' <summary>
    ''' 下载更新文件到暂存文件夹，并校验其有效性。失败会抛出异常。
    ''' </summary>
    Public Sub UpdateDownload(Info As UpdateInfo, Optional LoaderToSyncProgress As LoaderBase = Nothing)
        If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.DownloadUrl) Then
            Throw New Exception("没有可用的下载地址，请前往 Releases 页面手动下载。")
        End If
        Dim Folder = UpdateStageFolder
        DirectoryUtils.Delete(Folder)
        DirectoryUtils.Create(Folder)

        Dim IsZip = Info.DownloadUrl.EndsWithF(".zip", True)
        Dim DownloadPath = Folder & "Download" & If(IsZip, ".zip", ".exe")
        NetDownloadByLoader(Info.DownloadUrl, DownloadPath, LoaderToSyncProgress)

        Dim NewExe = Folder & "OpenPCL-new.exe"
        If IsZip Then
            Dim ExtractFolder = Folder & "Extract\"
            DirectoryUtils.Delete(ExtractFolder)
            FileUtils.ExtractToDirectory(DownloadPath, ExtractFolder)
            Dim Found = Directory.GetFiles(ExtractFolder, "*.exe", SearchOption.AllDirectories).FirstOrDefault
            If Found Is Nothing Then Throw New Exception("下载的压缩包中没有找到可执行文件。")
            FileUtils.Copy(Found, NewExe)
        Else
            FileUtils.Copy(DownloadPath, NewExe)
        End If
        FileUtils.Delete(DownloadPath)

        If Not FileUtils.Exists(NewExe) Then Throw New Exception("更新文件下载失败。")
        If FileUtils.GetInfo(NewExe).Length < 1024 * 100 Then Throw New Exception("下载的更新文件不完整。")
        Using Fs As New FileStream(NewExe, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim Header(1) As Byte
            If Fs.Read(Header, 0, 2) <> 2 OrElse Header(0) <> 77 OrElse Header(1) <> 90 Then
                Throw New Exception("下载的文件不是有效的 Windows 可执行文件。")
            End If
        End Using
        Logger.Info($"更新文件已准备完成：{NewExe}")
    End Sub

    ''' <summary>
    ''' 使用已下载的更新文件替换当前启动器，并重启。
    ''' </summary>
    Public Sub UpdateApplyAndRestart()
        Dim Folder = UpdateStageFolder
        Dim NewExe = Folder & "OpenPCL-new.exe"
        If Not FileUtils.Exists(NewExe) Then Throw New Exception("更新文件已被删除，请重新下载。")
        Dim Updater = Folder & "Updater.exe"
        FileUtils.Copy(NewExe, Updater)
        IsUpdateWaitingRestart = True
        UpdateRestart(True)
        FrmMain.EndProgram(False)
    End Sub

    ''' <summary>
    ''' 启动更新器进程，由它在当前进程退出后替换启动器文件。
    ''' </summary>
    Friend IsUpdateWaitingRestart As Boolean = False
    Private IsUpdaterStarted As Boolean = False

    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
        If IsUpdaterStarted Then Return
        Dim Updater = UpdateStageFolder & "Updater.exe"
        If Not FileUtils.Exists(Updater) Then Return
        IsUpdaterStarted = True
        Try
            StartProcess(Updater, $"--update {Process.GetCurrentProcess().Id} ""{PathExe}"" ""{Updater}"" {If(TriggerRestartAndByEnd, "1", "0")}")
        Catch ex As Exception
            IsUpdaterStarted = False
            Logger.Error(ex, "启动更新器失败")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 由更新器进程调用：等待旧进程退出后替换文件，可选地重新启动。
    ''' </summary>
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
        Dim FailedReason As String = ""
        '注意：更新器进程自身的 Paths.Base 指向暂存文件夹，因此这里必须使用更新文件所在的目录
        Dim StageFolder = Path.GetDirectoryName(NewFileName)
        If String.IsNullOrEmpty(StageFolder) Then StageFolder = Paths.Base & "PCL\Update"
        If Not StageFolder.EndsWith("\") Then StageFolder &= "\"
        Try
            Try
                Dim OldProcess = Process.GetProcessById(ProcessId)
                OldProcess.WaitForExit(30000)
            Catch ex As ArgumentException
                '旧进程已经退出
            End Try
            For i = 1 To 10
                Try
                    FileUtils.Copy(NewFileName, OldFileName)
                    FailedReason = ""
                    Exit For
                Catch ex As Exception
                    FailedReason = ex.Message
                    Thread.Sleep(400)
                End Try
            Next
            If FailedReason <> "" Then
                Try
                    FileUtils.Write(StageFolder & "UpdateFailed.txt", FailedReason)
                Catch ex As Exception
                    '无法写入失败原因时只能放弃提示
                End Try
            End If
            If TriggerRestart Then StartProcess(OldFileName)
        Catch ex As Exception
            '更新器进程中不进行任何 UI 操作
        End Try
    End Sub

    ''' <summary>
    ''' 启动时清理更新残留，并在上次更新失败时提示用户。
    ''' </summary>
    Public Sub UpdateStartupCleanup()
        Try
            Dim FailedFile = UpdateStageFolder & "UpdateFailed.txt"
            If FileUtils.Exists(FailedFile) Then
                Dim Reason = FileUtils.TryReadAsString(FailedFile)
                FileUtils.Delete(FailedFile)
                DirectoryUtils.Delete(UpdateStageFolder)
                MyMsgBox($"自动更新未能完成，可能是启动器文件被占用或没有写入权限。{vbCrLf}{vbCrLf}{Reason}{vbCrLf}{vbCrLf}你可以手动下载最新版本并替换当前程序。",
                         "自动更新失败", "打开 Releases", "取消", IsWarn:=True,
                         Button1Action:=Sub() OpenWebsite(UpdateReleasesPage))
                Return
            End If
            DirectoryUtils.Delete(UpdateStageFolder)
        Catch ex As Exception
            Logger.Warn(ex, "清理更新文件夹失败")
        End Try
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的启动器，它会被用于整合包打包。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        Dim Info = UpdateCheckRelease(False)
        If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.DownloadUrl) Then
            Throw New Exception("无法获取最新正式版的下载地址，请检查网络后重试。")
        End If
        Dim Target = PathTemp & "Latest.exe"
        If FileUtils.Exists(Target) AndAlso Info.Version IsNot Nothing Then
            Try
                Dim Existing = ParseVersion(FileVersionInfo.GetVersionInfo(Target).FileVersion)
                If Existing IsNot Nothing AndAlso Existing >= Info.Version Then Return
            Catch ex As Exception
                '无法读取版本信息时直接重新下载
            End Try
        End If
        NetDownloadByLoader(Info.DownloadUrl, Target, LoaderToSyncProgress)
    End Sub

#End Region

#Region "交互"

    ''' <summary>
    ''' 等待由更新提示触发的下载。设置页面加载后会读取并开始下载。
    ''' </summary>
    Public PendingUpdate As UpdateInfo

    ''' <summary>
    ''' 下载更新的加载器，由设置页面的更新卡片显示进度。
    ''' </summary>
    Public UpdateDownloadLoader As New LoaderTask(Of UpdateInfo, Boolean)("下载更新",
        Sub(Loader As LoaderTask(Of UpdateInfo, Boolean))
            UpdateDownload(Loader.Input, Loader)
            Loader.Output = True
        End Sub, Priority:=ThreadPriority.AboveNormal)

    Public Sub StartUpdateDownload(Info As UpdateInfo)
        If Info Is Nothing Then Return
        If UpdateDownloadLoader.State = LoadState.Loading Then Return
        UpdateDownloadLoader.Start(Info, IsForceRestart:=True)
    End Sub

    ''' <summary>
    ''' 由更新提示触发的下载：切换到设置页面的更新卡片后再开始下载，以便显示进度。
    ''' </summary>
    Public Sub StartUpdateFromUi(Info As UpdateInfo)
        If Info Is Nothing OrElse String.IsNullOrWhiteSpace(Info.DownloadUrl) Then
            OpenWebsite(If(Info Is Nothing, UpdateReleasesPage, Info.PageUrl))
            Return
        End If
        PendingUpdate = Info
        Try
            FrmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupSystem)
        Catch ex As Exception
            Logger.Warn(ex, "切换到设置页面失败")
        End Try
        '若页面已经加载过，Loaded 不会再触发，直接开始下载
        If UpdateDownloadLoader.State <> LoadState.Loading Then StartUpdateDownload(PendingUpdate)
        PendingUpdate = Nothing
    End Sub

    ''' <summary>
    ''' 显示更新提示。用户可以选择立即更新、暂不更新或查看详情。
    ''' </summary>
    Public Sub UpdatePrompt(Info As UpdateInfo)
        If Info Is Nothing Then Return
        Dim Text As New StringBuilder
        Text.AppendLine($"发现新版本：{Info.Title}")
        If Info.PublishTime.HasValue Then Text.AppendLine($"发布时间：{Info.PublishTime.Value:yyyy-MM-dd HH:mm}")
        Text.AppendLine($"当前版本：{VersionDisplay}")
        If Not String.IsNullOrWhiteSpace(Info.Changelog) Then
            Text.AppendLine()
            Text.AppendLine("更新说明：")
            Text.AppendLine(Info.Changelog)
        End If
        If String.IsNullOrWhiteSpace(Info.DownloadUrl) Then
            Text.AppendLine("该版本暂不支持自动下载，点击「立即更新」将在浏览器中打开下载页面。")
        End If
        Select Case MyMsgBox(Text.ToString, "发现新版本", "立即更新", "暂不更新", "查看详情",
                             Button3Action:=Sub() OpenWebsite(Info.PageUrl))
            Case 1
                StartUpdateFromUi(Info)
        End Select
    End Sub

    ''' <summary>
    ''' 从按钮或自定义事件触发一次更新检测。
    ''' </summary>
    Friend Sub UpdateCheckByButton()
        Hint("正在检查更新……")
        RunInNewThread(
        Sub()
            Try
                Dim Info = UpdateCheck(GetUpdateChannelSetting())
                RunInUi(
                Sub()
                    If Info Is Nothing OrElse Not Info.IsNewer Then
                        Hint($"当前已是最新版本（{Info?.VersionText}）", HintType.Green)
                    Else
                        UpdatePrompt(Info)
                    End If
                End Sub)
            Catch ex As Exception
                Logger.Error(ex, "检查更新失败")
                RunInUi(
                Sub()
                    If MyMsgBox($"检查更新失败：{vbCrLf}{ex.Message}{vbCrLf}{vbCrLf}你也可以在浏览器中打开 Releases 页面手动查看。",
                                "检查更新失败", "打开 Releases", "取消", IsWarn:=True) = 1 Then
                        OpenWebsite(UpdateReleasesPage)
                    End If
                End Sub)
            End Try
        End Sub, "UpdateCheck", ThreadPriority.BelowNormal)
    End Sub

    ''' <summary>
    ''' 启动时按设置自动检查更新，发现新版本时弹窗提示。
    ''' </summary>
    Public Sub UpdateAutoCheckStart()
        RunInNewThread(
        Sub()
            Try
                Thread.Sleep(8000) '等待启动流程基本完成
                If Not IsUpdateCheckDue() Then Return
                Dim Info = UpdateCheck(GetUpdateChannelSetting())
                If Info Is Nothing OrElse Not Info.IsNewer Then Return
                Dim Id = Info.VersionText & "|" & Info.Sha
                If Settings.Get(Of String)("CacheUpdateHinted") = Id Then Return '同一版本只提示一次
                Settings.Set("CacheUpdateHinted", Id)
                RunInUi(Sub() UpdatePrompt(Info))
            Catch ex As Exception
                Logger.Warn(ex, "自动检查更新失败")
            End Try
        End Sub, "UpdateAutoCheck", ThreadPriority.BelowNormal)
    End Sub

#End Region

End Module
