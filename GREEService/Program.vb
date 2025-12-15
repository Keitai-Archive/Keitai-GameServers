Imports System.Collections.Specialized
Imports System.Formats.Asn1.AsnWriter
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Web
Imports Npgsql

Module Program
    Dim PORT As Integer = 8100
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()
    Dim TABLE_NAME As String = "gree_best_scores"

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        Try
            EnsureGreeBestScoresTable()
            Dim listener As New HttpListener()
            listener.Prefixes.Add($"http://*: {PORT}/".Replace(" ", ""))
            listener.Start()
            Console.WriteLine($"Server listening on http://127.0.0.1:{PORT}/")
            While True
                Dim context = listener.GetContext()
                Threading.ThreadPool.QueueUserWorkItem(AddressOf HandleRequest, context)
            End While
        Catch ex As Exception
            Console.WriteLine("Fatal error: " & ex.ToString())
        End Try
    End Sub
    Private Sub HandleRequest(state As Object)
        Dim context = DirectCast(state, HttpListenerContext)
        Try
            Dim request = context.Request
            Dim QueryStrings As Specialized.NameValueCollection = LogRequest(request)
            Dim apireq As ApiRequest = ApiRequest.FromQuery(QueryStrings)
            Dim path = request.Url.AbsolutePath

            'Router
            Select Case path.ToLowerInvariant()
                'Game Routes
                Case "/" 'Some Apps use this, and will have the path in a query.
                    ' Root Websites
                    If apireq.Mode = "ranking" AndAlso apireq.Act = "view" Then
                        HandleWebsite_GreeRankingPage(context, apireq)
                        Return
                    End If

                    Select Case apireq.action
                        Case "api_cp_auth"
                            HandleRoot_API_CP_Auth(context, apireq)
                        Case "api_cp_ranking_add"
                            HandleRoot_API_CP_Ranking_Add(context, apireq)
                    End Select

                Case "/api/auth_user.php"
                    Select Case apireq.App 'Certain games have different auth but same URLs
                        Case "sea_oki_oki_sub"
                            HandleOkiOkiAuth_User(context, apireq)
                        Case Else
                            HandleAuth_User(context, apireq)
                    End Select
                Case "/api/keep_alive.php"
                    HandleKeep_Alive(context, apireq)
                Case "/api/leave_seat.php"
                    HandleLeave_Seat(context, apireq)
                Case "/api/upload_result.php"
                    HandleUploadResult(context, apireq)
                Case "/api/app_joint.php"
                    HandleApp_Joint(context, apireq)

                    'Website Routes on Specific Paths
                Case "/gacha"
                    HandleWebsite_Gacha(context, apireq)
                Case "/shop"
                    HandleWebsite_Shop(context, apireq)
                Case "/r/11669/1", "/r/11562/1", "/r/11563/1", "/r/11161/1"
                    HandleWebsite_GreeRankingPage(context, apireq)
            End Select

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Try
                SendTextResponse(DirectCast(state, HttpListenerContext), "Internal Server Error", 500)
            Catch
                ' ignore
            End Try
        End Try
    End Sub

    'Game Handlers
    Private Sub HandleRoot_API_CP_Auth(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request
        Dim responsestring As String = "ret=0"
        Console.WriteLine("Responding with: " & responsestring)
        SendTextResponse(context, responsestring, 200)
    End Sub
    Private Sub HandleRoot_API_CP_Ranking_Add(context As HttpListenerContext, apireq As ApiRequest)
        Dim uid As String = apireq.UID
        Dim idRaw As String = apireq.id  'game id
        Dim pointRaw As String = apireq.Point
        Dim aplChk As String = apireq.aplChk
        Dim score As Integer = apireq.Point

        Integer.TryParse(pointRaw, score)
        Dim responsestring As String = "ret=0"

        If Not String.IsNullOrWhiteSpace(idRaw) AndAlso Not String.IsNullOrWhiteSpace(uid) Then
            UpsertBestScore(idRaw, uid, score, aplChk)
            Console.WriteLine($"RankingAdd game_id={idRaw} uid={uid} score={score} apl_chk={aplChk} -> {responsestring}")
        Else
            Console.WriteLine($"RankingAdd missing/invalid game_id or uid (id='{idRaw}', uid='{uid}') -> {responsestring}")
        End If

        SendTextResponse(context, responsestring, 200)
    End Sub
    Private Sub HandleAuth_User(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request

        'Pachislot Vars
        Dim nowEpoch As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

        ' result -> Handle.getResult()
        ' Used to control flow (0=ok,101=NonMember,103=Banned,201=AppVerUpdate)
        Dim result As Integer = 0
        ' dollar -> GreeUtil.m_gree_dollar = getInteger("dollar")
        Dim dollar As Integer = 1000
        ' tc -> GreeUtil.m_gree_tc = getInteger("tc")
        Dim tc As Integer = 1000
        ' text -> GreeUtil.m_gree_news = getString("text")
        ' Shown as server/news message
        Dim text As String = "Welcome to Pachislot Haven"
        ' mode -> GreeUtil.m_greeMode = getInteger("mode")
        Dim mode As Integer = 0
        ' coin -> PlayData.p_hasball = getInteger("coin")
        Dim coin As Integer = 1000
        ' total_roll -> PlayData.p_allrpm = getInteger("total_roll")
        Dim totalRoll As Integer = 0
        ' roll -> PlayData.p_nowrpm = getInteger("roll")
        Dim roll As Integer = 0
        ' big -> PlayData.p_hitcnt = getInteger("big")
        Dim big As Integer = 0
        ' small -> PlayData.p_khitcnt = getInteger("small")
        Dim small As Integer = 0
        ' at -> GreeUtil.m_at_value = getInteger("at")
        Dim atValue As Integer = 0
        ' time -> GreeUtil.m_serverTime = getLong("time") * 1000
        ' MUST be epoch seconds (not ms)
        Dim serverTimeSeconds As Long = nowEpoch

        Dim json As String =
    "{" &
    """result"":""" & result & """," &
    """dollar"":""" & dollar & """," &
    """tc"":""" & tc & """," &
    """text"":""" & JsonEsc(text) & """," &
    """mode"":""" & mode & """," &
    """coin"":""" & coin & """," &
    """total_roll"":""" & totalRoll & """," &
    """roll"":""" & roll & """," &
    """big"":""" & big & """," &
    """small"":""" & small & """," &
    """at"":""" & atValue & """," &
    """time"":""" & serverTimeSeconds & """" &
    "}"


        Dim buf = System.Text.Encoding.UTF8.GetBytes(json)
        Console.WriteLine("Responding with: " & json)
        SendJsonResponse(context, json, 200)
    End Sub
    Private Sub HandleKeep_Alive(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request
        Dim json As String = "{""result"":""0"",""telop"":""""}"
        Dim buf = System.Text.Encoding.UTF8.GetBytes(json)
        Console.WriteLine("Responding with: " & json)
        SendJsonResponse(context, json, 200)
    End Sub
    Private Sub HandleLeave_Seat(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request

        Dim result As Integer = 0
        ' url_gacha
        Dim urlGacha As String = "http://localhost/gacha"
        ' url_shop
        Dim urlShop As String = "http://localhost/shop"
        Dim json As String =
    "{" &
    """result"":""" & result & """," &
    """url_gacha"":""" & JsonEsc(urlGacha) & """," &
    """url_shop"":""" & JsonEsc(urlShop) & """" &
    "}"

        Dim buf = System.Text.Encoding.UTF8.GetBytes(json)
        Console.WriteLine("Responding with: " & json)
        SendJsonResponse(context, json, 200)
    End Sub
    Private Sub HandleUploadResult(context As HttpListenerContext, apireq As ApiRequest)
        Dim form = System.Web.HttpUtility.ParseQueryString("")
        If context.Request.HasEntityBody Then
            Using sr As New IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding)
                form = System.Web.HttpUtility.ParseQueryString(sr.ReadToEnd())
            End Using
        End If

        ' Incoming fields (from queryUploadResult_)
        Dim user As String = form("user")          ' GreeUtil.m_userID
        Dim session As String = form("session")    ' GreeUtil.m_sessionID
        Dim reqId As String = form("req_id")       ' request ID

        ' store stats:
        Dim coin As Integer = Val(form("coin"))
        Dim totalRoll As Integer = Val(form("total_roll"))
        Dim roll As Integer = Val(form("roll"))
        Dim big As Integer = Val(form("big"))
        Dim small As Integer = Val(form("small"))
        Dim atValue As Integer = Val(form("at"))

        ' Response
        Dim result As Integer = 0
        ' url_goto -> read by getString("url_goto") when result == 0
        ' App will WebTo(this.m_webtoUrl) in mode 93
        Dim urlGoto As String = "http://localhost/goodbye"

        Dim json As String =
        "{" &
        """result"":""" & result & """," &
        """url_goto"":""" & JsonEsc(urlGoto) & """" &
        "}"
        Console.WriteLine("Responding with: " & json)
        SendJsonResponse(context, json, 200) ' your non-chunked one-line JSON sender
    End Sub
    Private Sub HandleApp_Joint(context As HttpListenerContext, apireq As ApiRequest)
        Dim form = System.Web.HttpUtility.ParseQueryString("")
        If context.Request.HasEntityBody Then
            Using sr As New IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding)
                form = System.Web.HttpUtility.ParseQueryString(sr.ReadToEnd())
            End Using
        End If

        ' --- Request fields from the client (case 11) ---
        Dim coin As Integer = Val(form("coin"))
        Dim totalRoll As Integer = Val(form("total_roll"))
        Dim roll As Integer = Val(form("roll"))
        Dim small As Integer = Val(form("small"))
        Dim atValue As Integer = Val(form("at"))
        Dim reqId As String = form("req_id")

        Dim moveTo As String = form("move_to")         ' destination/seat/room
        Dim jointParams As String = form("joint_params") ' may be "null"

        Dim issItem As String = form("iss_item")       ' may be "null"
        Dim issReqId As String = form("iss_req_id")    ' may be "null"
        Dim issStatus As String = form("iss_status")   ' may be "null"

        Dim json As String = "{""result"":0}"
        Console.WriteLine("Responding with: " & json)

        SendJsonResponse(context, json, 200) ' your one-line, non-chunked sender
    End Sub

    'Game Specific Handlers
    Private Sub HandleOkiOkiAuth_User(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request

        'Pachislot Vars
        Dim nowEpoch As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

        Dim result As Integer = 401              ' l.a("result")
        Dim mode As Integer = 0                ' l.a("mode")

        Dim user As String = "TESTUSER"        ' l.c("user") -> later POSTed as user=...
        Dim session As String = "TESTSESSION"  ' l.c("session") -> later POSTed as session=...

        Dim sessTime As Integer = 0        ' l.a("sess_time") -> this.n.g.a(...)
        Dim path As String = ""                ' l.c("path") -> banner/resource path
        Dim text As String = ""                ' l.b("text") -> this.n.s (news/message)

        Dim dollar As Integer = 1000              ' l.a("dollar")
        Dim coin As Integer = 1000                ' l.a("coin")
        Dim conf As Integer = 0                ' l.a("conf") (clamped 0..5 by client)
        Dim totalRoll As Integer = 0           ' l.a("total_roll")
        Dim roll As Integer = 0                ' l.a("roll")
        Dim big As Integer = 0                 ' l.a("big")
        Dim small As Integer = 0               ' l.a("small")
        Dim atValue As Integer = 0             ' l.a("at")
        Dim tc As Integer = 0                  ' l.a("tc")

        Dim jointParams As String = "null"     ' l.c("joint_params")

        Dim serverTimeSeconds As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds() ' getLong("time") path elsewhere

        Dim json As String =
    "{" &
    """result"":" & result & "," &
    """mode"":" & mode & "," &
    """user"":""" & JsonEsc(user) & """," &
    """session"":""" & JsonEsc(session) & """," &
    """sess_time"":" & sessTime & "," &
    """path"":""" & JsonEsc(path) & """," &
    """text"":""" & JsonEsc(text) & """," &
    """dollar"":" & dollar & "," &
    """coin"":" & coin & "," &
    """conf"":" & conf & "," &
    """total_roll"":" & totalRoll & "," &
    """roll"":" & roll & "," &
    """big"":" & big & "," &
    """small"":" & small & "," &
    """at"":" & atValue & "," &
    """tc"":" & tc & "," &
    """joint_params"":""" & JsonEsc(jointParams) & """," &
    """time"":" & serverTimeSeconds &
    "}"


        Dim buf = System.Text.Encoding.UTF8.GetBytes(json)
        Console.WriteLine("Responding with: " & json)
        SendJsonResponse(context, json, 200)
    End Sub

    'Website Handlers
    Private Sub HandleWebsite_Gacha(context As HttpListenerContext, apireq As ApiRequest)
        Dim ResponseString = "No Gacha to see here"
        Console.WriteLine("Responding with: " & ResponseString)
        SendTextResponse(context, ResponseString, 200)
    End Sub
    Private Sub HandleWebsite_Shop(context As HttpListenerContext, apireq As ApiRequest)
        Dim ResponseString = "No Shop to see here"
        Console.WriteLine("Responding with: " & ResponseString)
        SendTextResponse(context, ResponseString, 200)
    End Sub
    Private Sub HandleWebsite_GreeRankingPage(context As HttpListenerContext, apireq As ApiRequest)
        Dim request = context.Request
        Dim qs = request.QueryString

        Dim gameIdRaw As String = qs("game_id")
        Dim grd As String = qs("_grd") ' optional, displayed only

        Dim sb As New Text.StringBuilder()
        sb.Append("<!DOCTYPE html><html><head>")
        sb.Append("<meta charset='Shift_JIS'>")
        sb.Append("<title>Ranking</title>")
        sb.Append("</head><body>")
        sb.Append("<h2>ランキング</h2>")

        Dim gameId As Integer

        ' If no game_id specified, show index page
        If String.IsNullOrWhiteSpace(gameIdRaw) OrElse Not Integer.TryParse(gameIdRaw, gameId) Then
            sb.Append("<p>ゲームを選択してください。</p>")
            If Not String.IsNullOrWhiteSpace(grd) Then
                sb.Append("<p><small>_grd=" & Html(grd) & "</small></p>")
            End If

            Dim games = Db_ListGames()
            If games.Count = 0 Then
                sb.Append("<p>まだスコアがありません。</p>")
            Else
                sb.Append("<table border='1' cellpadding='4' cellspacing='0'>")
                sb.Append("<tr><th>game_id</th><th>トップUID</th><th>トップスコア</th></tr>")
                For Each g In games
                    sb.Append("<tr>")
                    sb.Append("<td><a href='/?mode=ranking&act=view&game_id=" & g.gameId & "'>" & g.gameId & "</a></td>")
                    sb.Append("<td>" & Html(g.topUid) & "</td>")
                    sb.Append("<td>" & g.topScore & "</td>")
                    sb.Append("</tr>")
                Next
                sb.Append("</table>")
            End If

            sb.Append("</body></html>")
            SendHtmlResponse(context, sb.ToString(), 200)
            Return
        End If

        ' Leaderboard view
        sb.Append("<p><b>game_id:</b> " & gameId & "</p>")
        sb.Append("<p><a href='/?mode=ranking&act=view'>← 戻る</a></p>")

        Dim rows = Db_GetLeaderboard(gameId, 50)
        If rows.Count = 0 Then
            sb.Append("<p>このゲームのスコアがありません。</p>")
        Else
            sb.Append("<table border='1' cellpadding='4' cellspacing='0'>")
            sb.Append("<tr><th>順位</th><th>UID</th><th>スコア</th></tr>")
            Dim rank As Integer = 1
            For Each row In rows
                sb.Append("<tr>")
                sb.Append("<td>" & rank & "</td>")
                sb.Append("<td>" & Html(row.uid) & "</td>")
                sb.Append("<td>" & row.score & "</td>")
                sb.Append("</tr>")
                rank += 1
            Next
            sb.Append("</table>")
        End If

        sb.Append("</body></html>")
        SendHtmlResponse(context, sb.ToString(), 200)
    End Sub
    Private Sub HandleWebsite_SitePage(context As HttpListenerContext, apireq As ApiRequest)
        Dim html As String =
"<!DOCTYPE html>" &
"<html><head>" &
"<meta charset='Shift_JIS'>" &
"<title>Site Page</title>" &
"</head><body>" &
"<h2>Site Page</h2>" &
"<p>現在ランキングは利用できません。</p>" &
"<h2>Site Page</h2>" &
"<p>Not implemented yet.</p>" &
"</body></html>"

        Dim bytes = Text.Encoding.GetEncoding("shift_jis").GetBytes(html)

        Dim resp = context.Response
        resp.StatusCode = 200
        resp.SendChunked = False
        resp.ContentType = "text/html"
        resp.ContentEncoding = Text.Encoding.GetEncoding("shift_jis")
        resp.ContentLength64 = bytes.Length
        resp.OutputStream.Write(bytes, 0, bytes.Length)
        resp.OutputStream.Close()
    End Sub

    'DB Helpers
    Public Sub EnsureGreeBestScoresTable()
        Const sql As String =
"
CREATE TABLE IF NOT EXISTS public.gree_best_scores (
    game_id     INTEGER     NOT NULL,        -- id=88001 (game / campaign id)
    uid         TEXT        NOT NULL,         -- player UID
    best_score  INTEGER     NOT NULL DEFAULT 0,
    apl_chk     TEXT        NULL,             -- optional metadata
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (game_id, uid)
);

CREATE INDEX IF NOT EXISTS idx_gree_best_scores_gameid_score
ON public.gree_best_scores (game_id, best_score DESC);
"

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub
    Private Sub UpsertBestScore(gameId As Integer, uid As String, score As Integer, Optional aplChk As String = Nothing)
        Const sql As String =
        "INSERT INTO public.gree_best_scores (game_id, uid, best_score, apl_chk, created_at, updated_at) " &
        "VALUES (@game_id, @uid, @score, @apl_chk, now(), now()) " &
        "ON CONFLICT (game_id, uid) DO UPDATE SET " &
        "  best_score = GREATEST(public.gree_best_scores.best_score, EXCLUDED.best_score), " &
        "  apl_chk = COALESCE(EXCLUDED.apl_chk, public.gree_best_scores.apl_chk), " &
        "  updated_at = now();"

        Using conn As New Npgsql.NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Using cmd As New Npgsql.NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@game_id", gameId)
                cmd.Parameters.AddWithValue("@uid", uid)
                cmd.Parameters.AddWithValue("@score", score)
                If String.IsNullOrWhiteSpace(aplChk) Then
                    cmd.Parameters.AddWithValue("@apl_chk", DBNull.Value)
                Else
                    cmd.Parameters.AddWithValue("@apl_chk", aplChk)
                End If
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub
    Private Function Db_ListGames() As List(Of (gameId As Integer, topScore As Integer, topUid As String))
        Dim results As New List(Of (Integer, Integer, String))

        Const sql As String =
        "SELECT game_id, best_score, uid " &
        "FROM (" &
        "  SELECT game_id, uid, best_score, " &
        "         ROW_NUMBER() OVER (PARTITION BY game_id ORDER BY best_score DESC, updated_at ASC) AS rn " &
        "  FROM public.gree_best_scores" &
        ") t " &
        "WHERE rn = 1 " &
        "ORDER BY best_score DESC, game_id ASC;"

        Using conn As New Npgsql.NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Using cmd As New Npgsql.NpgsqlCommand(sql, conn)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        results.Add((r.GetInt32(0), r.GetInt32(1), r.GetString(2)))
                    End While
                End Using
            End Using
        End Using

        Return results
    End Function
    Private Function Db_GetLeaderboard(gameId As Integer, Optional limit As Integer = 50) As List(Of (uid As String, score As Integer))
        Dim results As New List(Of (String, Integer))

        Const sql As String =
        "SELECT uid, best_score " &
        "FROM public.gree_best_scores " &
        "WHERE game_id = @game_id " &
        "ORDER BY best_score DESC, updated_at ASC " &
        "LIMIT @lim;"

        Using conn As New Npgsql.NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            Using cmd As New Npgsql.NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@game_id", gameId)
                cmd.Parameters.AddWithValue("@lim", limit)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        results.Add((r.GetString(0), r.GetInt32(1)))
                    End While
                End Using
            End Using
        End Using

        Return results
    End Function


    'HTTP Response Helpers
    Private Sub SendTextResponse(context As HttpListenerContext, body As String, statusCode As Integer)
        Dim bytes = Encoding.UTF8.GetBytes(body)

        context.Response.StatusCode = statusCode
        context.Response.ContentType = "text/plain; charset=utf-8"
        context.Response.ContentEncoding = Encoding.UTF8
        context.Response.ContentLength64 = bytes.Length

        Using output = context.Response.OutputStream
            output.Write(bytes, 0, bytes.Length)
        End Using
    End Sub
    Private Sub SendJsonResponse(context As HttpListenerContext, json As String, Optional statusCode As Integer = 200)
        Dim buf = Text.Encoding.ASCII.GetBytes(json)

        Dim resp = context.Response
        resp.StatusCode = statusCode
        resp.SendChunked = False
        resp.KeepAlive = False
        resp.ContentType = "application/json"
        resp.ContentEncoding = Text.Encoding.ASCII
        resp.ContentLength64 = buf.Length

        resp.OutputStream.Write(buf, 0, buf.Length)
        resp.OutputStream.Close()
        resp.Close()
    End Sub
    Private Sub SendHtmlResponse(context As HttpListenerContext, html As String, Optional statusCode As Integer = 200)
        Dim enc = Text.Encoding.GetEncoding("shift_jis")
        Dim bytes = enc.GetBytes(html)

        Dim resp = context.Response
        resp.StatusCode = statusCode
        resp.SendChunked = False
        resp.KeepAlive = False
        resp.ContentType = "text/html"
        resp.ContentEncoding = enc
        resp.ContentLength64 = bytes.Length

        resp.OutputStream.Write(bytes, 0, bytes.Length)
        resp.OutputStream.Close()
        resp.Close()
    End Sub
    Private Function LogRequest(request As HttpListenerRequest) _
        As System.Collections.Specialized.NameValueCollection

        Console.WriteLine("--------------------------------------------------")
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Incoming Request")
        Console.WriteLine($"{request.HttpMethod} {request.Url}")

        Console.WriteLine($"Remote: {request.RemoteEndPoint?.Address}:{request.RemoteEndPoint?.Port}")
        Console.WriteLine($"Local:  {request.LocalEndPoint?.Address}:{request.LocalEndPoint?.Port}")

        Console.WriteLine($"Path:   {request.Url.AbsolutePath}")
        Console.WriteLine($"Query:  {request.Url.Query}")

        ' --- Combined params (URL + BODY) ---
        Dim allParams As New Specialized.NameValueCollection()

        ' ---- URL query parameters ----
        If request.Url.Query.Length > 1 Then
            Dim qs = HttpUtility.ParseQueryString(request.Url.Query)
            For Each key As String In qs.AllKeys
                allParams(key) = qs(key)
            Next
        End If

        ' ---- Headers ----
        Console.WriteLine("Headers:")
        For Each key As String In request.Headers.AllKeys
            Console.WriteLine($"  {key}: {request.Headers(key)}")
        Next

        ' ---- Body (POST / PUT form data) ----
        Dim body As String = Nothing

        If request.HasEntityBody Then
            Using reader As New StreamReader(request.InputStream, request.ContentEncoding)
                body = reader.ReadToEnd()
            End Using

            If Not String.IsNullOrWhiteSpace(body) Then
                Console.WriteLine("Body:")
                Console.WriteLine(body)

                ' ---- DoJa-tolerant parse ----
                ' 1) remove control chars (except CR/LF/TAB)
                Dim cleaned As String = Regex.Replace(body, "[\u0000-\u0008\u000B\u000C\u000E-\u001F]", "")

                ' 2) if body is newline-delimited, normalize to '&'
                Dim normalized As String = cleaned.Trim() _
            .Replace(vbCrLf, "&") _
            .Replace(vbLf, "&") _
            .Replace(vbCr, "&")

                ' 3) Parse as querystring
                Dim bodyParams = HttpUtility.ParseQueryString(normalized)

                For Each key As String In bodyParams.AllKeys
                    If key Is Nothing Then Continue For
                    allParams(key) = bodyParams(key)
                Next
            End If
        End If

        ' ---- Dump merged parameters ----
        If allParams.Count > 0 Then
            Console.WriteLine("Parsed Parameters (URL + BODY):")
            For Each key As String In allParams.AllKeys
                Console.WriteLine($"  {key} = {allParams(key)}")
            Next
        End If

        Console.WriteLine("--------------------------------------------------")

        Return allParams
    End Function

    'Helpers
    Private Function JsonEsc(s As String) As String
        If s Is Nothing Then Return ""
        Return s.Replace("\", "\\") _
            .Replace("""", "\""") _
            .Replace(vbCrLf, "\n") _
            .Replace(vbLf, "\n")
    End Function
    Private Function Html(s As String) As String
        If s Is Nothing Then Return ""
        Return System.Net.WebUtility.HtmlEncode(s)
    End Function

End Module