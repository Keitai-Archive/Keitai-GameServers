Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Web
Imports System.Text.RegularExpressions
Imports Npgsql

Module Program
    Dim PORT As Integer = 8098
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()
    Dim TABLE_NAME As String = "mobage_best_scores"

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Try
            'EnsureLeaderboardTable()
            Dim listener As New HttpListener()
            listener.Prefixes.Add($"http://*:{PORT}/")
            listener.Start()
            Console.WriteLine($"Server listening on http://127.0.0.1:{PORT}/")
            Console.WriteLine("Press Ctrl+C to stop.")

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
            Dim path = request.Url.AbsolutePath.ToLowerInvariant()
            Select Case True
                'Mobage Normal Handlers
                Case path = "/-global/api/authorize"
                    HandleAuthorize(context, QueryStrings)
                Case path = "/-global/api/gettime"
                    HandleGetTime(context, QueryStrings)
                Case path.Contains("initgame")
                    HandleInitGame(context, QueryStrings)
                Case path = "/-global/api/savescore"
                    HandleSaveScore(context, QueryStrings)
                Case path = "/-global/api/getranking2"
                    HandleGetRanking2(context, QueryStrings)
                Case path = "/-global/api/otonagegetscore"
                    HandleOtonageGetScore(context, QueryStrings)
                Case path = "/-global/api/otonagesavescore"
                    HandleOtonageSaveScore(context, QueryStrings)

                'Mobage M7 Pachislot Handlers
                Case path = "/game00/_api_auth_user2.cgi"
                    HandleM7Authorize(context, QueryStrings)
                Case path = "/game00/game.cgi", path = "/game00/game.fcgi"
                    HandleGameCgi(context, QueryStrings)

                'Mobage Town Links
                Case path = "/_ele_t"
                    HandleMobageTown(context, QueryStrings)

                Case Else
                    context.Response.StatusCode = 404
                    context.Response.Close()
            End Select
        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Return
        End Try
    End Sub

    'Normal Mobage Handlers
    Private Sub HandleAuthorize(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 

        ' Determine if version update is needed
        Dim expectedVer As String = ver
        Dim mustUpdate As Boolean = (String.IsNullOrEmpty(ver) OrElse ver <> expectedVer)
        Dim responseBody As String
        If mustUpdate Then
            responseBody = "VERUP;" & vbLf
        Else
            ' AUTH;<status_int>;<c>;<d>
            ' Use stable tokens. Keep them ASCII-safe.
            Dim tokenC As String = "C_" & If(ser, "")
            Dim tokenD As String = "D_" & If(icc, "")

            Dim sb As New Text.StringBuilder()
            'Seems like the 2nd var here is a Mobage Specific UID that only numbers
            sb.Append("AUTH;1;").Append(tokenC).Append(";").Append(tokenD).Append(";").Append(vbLf)
            responseBody = sb.ToString()
        End If
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleGetTime(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 


        Dim responseBody = "123456789"
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleInitGame(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 


        Dim responseBody = "1"
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleGetRanking2(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim c = QueryStrings("c") ' Difficulty Mode
        Dim m = QueryStrings("m") ' Today/Month/Alltime Mode
        Dim lf As String = vbLf
        Dim q As Integer = 0
        Dim o As Integer = 1
        Dim rankcount As Integer = 3 '4 Seems to be the max


        ' Build Logic to Get Ranks from DB
        Dim sb As New Text.StringBuilder()
        sb.Append(q).Append(lf)
        sb.Append(o).Append(lf)
        sb.Append(rankcount).Append(lf)
        For i As Integer = 0 To rankcount
            sb.Append($"{rankcount};100;test{rankcount};9999;").Append(lf)
        Next
        Console.WriteLine($"Response: {sb.ToString()}")
        SendTextResponse(context, sb.ToString(), 200)
    End Sub
    Private Sub HandleOtonageGetScore(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 

        ' Need to figure out what the rest of these are for.
        Dim responseBody = $"OK;1;2;3;4;" & vbLf
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleOtonageSaveScore(context As HttpListenerContext, p As Specialized.NameValueCollection)
        ' Parse
        Dim uidStr As String = p(".uid")
        Dim gidStr As String = p(".gid")
        Dim scoreStr As String = p("s")

        Dim gameId As Integer
        Integer.TryParse(gidStr, gameId)

        Dim score As Integer
        Integer.TryParse(scoreStr, score)

        Dim userId As String = If(String.IsNullOrWhiteSpace(uidStr), "0", uidStr)

        'UpsertBestScore(gameId, userId, terminalId, score)
        Console.WriteLine($"OtonageSaveScore: gid={gameId.ToString} score={score.ToString} uid={userId.ToString}")
        Dim body As String = $"ERR;Score's not implemented yet.;" & vbLf
        SendTextResponse(context, body, 200)
    End Sub
    Private Sub HandleSaveScore(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id

        'Get and Save Score
        Dim scoreStr As String = QueryStrings("s")
        Dim score As Integer = 0
        Integer.TryParse(scoreStr, score)
        Console.WriteLine($"SaveScore: gid={gid.ToString} ver={ver.ToString} score={score.ToString} icc(uid)={icc.ToString} ser(terminal-id)={ser.ToString}")

        'Store best score in DB
        UpsertBestScore(gid, icc, ser, score)
        Dim responseBody = $"OK{vbLf}"
        ' (client does not care about response contents)
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleGetAvatar(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 

        Dim responseBody = "OK;OK;OK;OK;OK;"
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleMobageTown(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim responseBody As String = "Mobage Town not implemented yet..."
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub

    'M7 Mobage handlers
    Private Sub HandleM7Authorize(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim ua = QueryStrings("ua")
        Dim raw = QueryStrings("raw") ' "1"
        Dim gid = QueryStrings("gid") ' GameID
        Dim ver = QueryStrings("ver") ' GameVersion
        Dim ser = QueryStrings("ser") ' terminal-id
        Dim icc = QueryStrings("icc") ' user-id
        Dim sid = QueryStrings("sid") ' 

        Dim responseBody As String
        responseBody = "OK" & vbLf & "999" & vbLf
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
    End Sub
    Private Sub HandleGameCgi(context As HttpListenerContext, p As Specialized.NameValueCollection)
        Dim mUid As String = p("m_uid")
        Dim aid As String = p("aid")
        Dim ver As String = p("ver")
        Dim appVer As String = p("app_ver")
        Dim nid As String = p("nid")

        Console.WriteLine($"game.cgi: nid={nid} aid={aid} ver={ver} app_ver={appVer} m_uid={mUid}")
        Dim responseBody = "OK" & vbLf & "999" & vbLf & "999" & vbLf

        SendTextResponse(context, responseBody, 200)
    End Sub

    'Database
    Private Sub EnsureLeaderboardTable()
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Dim sql As String = $"
            CREATE TABLE IF NOT EXISTS mobage_best_scores (
    game_id     INTEGER NOT NULL,
    user_id     TEXT    NOT NULL,
    terminal_id TEXT    NOT NULL,
    best_score  INTEGER NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (game_id, user_id)
);

CREATE INDEX IF NOT EXISTS idx_mobage_best_scores_game_best
    ON mobage_best_scores (game_id, best_score DESC); "

            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
            Dim idxSql As String = $"
            CREATE INDEX IF NOT EXISTS idx_{TABLE_NAME}_score
            ON {TABLE_NAME} (score DESC);
        "

            Using idxCmd As New NpgsqlCommand(idxSql, conn)
                idxCmd.ExecuteNonQuery()
            End Using
            Console.WriteLine("DB Connected & Leaderboard table ensured.")
        End Using
    End Sub
    Private Sub UpsertBestScore(gameId As Integer,
                            userId As String,
                            terminalId As String,
                            score As Integer)

        If String.IsNullOrWhiteSpace(userId) Then Throw New ArgumentException("userId is required")
        If String.IsNullOrWhiteSpace(terminalId) Then terminalId = ""
        If score < 0 Then score = 0

        Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()

        Const sql As String =
"INSERT INTO mobage_best_scores (game_id, user_id, terminal_id, best_score, updated_at)
 VALUES (@game_id, @user_id, @terminal_id, @score, NOW())
 ON CONFLICT (game_id, user_id)
 DO UPDATE SET
   best_score  = GREATEST(mobage_best_scores.best_score, EXCLUDED.best_score),
   terminal_id = EXCLUDED.terminal_id,
   updated_at  = NOW();"

        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()

            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@game_id", gameId)
                cmd.Parameters.AddWithValue("@user_id", userId)
                cmd.Parameters.AddWithValue("@terminal_id", terminalId)
                cmd.Parameters.AddWithValue("@score", score)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    'HTTP Response
    Private Sub SendTextResponse(context As HttpListenerContext, body As String, statusCode As Integer)
        Dim response = context.Response
        Dim bytes = Text.Encoding.ASCII.GetBytes(If(body, ""))
        response.StatusCode = statusCode
        response.Headers("X-API-Status") = "OK"
        response.ContentType = "text/plain"
        response.ContentLength64 = bytes.Length
        response.OutputStream.Write(bytes, 0, bytes.Length)
        response.OutputStream.Close()
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
End Module
