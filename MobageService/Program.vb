Imports System.IO
Imports System.Net
Imports System.Runtime.ConstrainedExecution
Imports System.Text
Imports System.Web
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
            Dim path = request.Url.AbsolutePath
            Select Case path.ToLower
                Case "/-global/api/authorize"
                    HandleAuthorize(context, QueryStrings)
                Case "/-global/api/savescore"
                    HandleSaveScore(context, QueryStrings)
            End Select

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Return
        End Try
    End Sub
    Private Sub HandleAuthorize(context As HttpListenerContext, QueryStrings As Specialized.NameValueCollection)
        Dim raw = QueryStrings(".raw") ' "1"
        Dim gid = QueryStrings(".gid") ' GameID
        Dim ver = QueryStrings(".ver") ' GameVersion
        Dim ser = QueryStrings(".ser") ' terminal-id
        Dim icc = QueryStrings(".icc") ' user-id
        Dim sid = QueryStrings(".sid") ' 

        ' Determine if version update is needed
        Dim expectedVer As String = "1000"
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
            sb.Append("AUTH;1;").Append(tokenC).Append(";").Append(tokenD).Append(";").Append(vbLf)
            responseBody = sb.ToString()
        End If
        Console.WriteLine($"Response: {responseBody}")
        SendTextResponse(context, responseBody, 200)
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

                ' Parse x-www-form-urlencoded body
                If request.ContentType IsNot Nothing AndAlso
                   request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) Then

                    Dim bodyParams = HttpUtility.ParseQueryString(body)
                    For Each key As String In bodyParams.AllKeys
                        allParams(key) = bodyParams(key)
                    Next
                End If
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
