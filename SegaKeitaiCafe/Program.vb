Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Web
Imports Npgsql

Module ScoreServer
    Dim PORT As Integer = 8099
    Dim CONNECTION_STRING As String = File.ReadAllText("db_connection_string.cfg").Trim()

    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Try
            EnsureTables()
            Dim listener As New HttpListener()
            listener.Prefixes.Add($"http://*:{PORT}/")
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
            Dim path = request.Url.AbsolutePath.ToLowerInvariant()

            Select Case path
                Case "/cafe/servlet/tennis_ul"
                    HandleTennisUl(context)

                Case "/cafe/servlet/horumon_ul"
                    HandleHorumonUl(context)

                Case "/cafe/servlet/block2_ul"
                    HandleBlock2Ul(context)

                Case "/cafe/servlet/daifugou_ul"
                    HandleDaifugouUl(context)
            End Select

        Catch ex As Exception
            Console.WriteLine("Request error: " & ex.ToString())
            Try
                ' Safe fallback (generic)
                SendTextResponse(context, "0:0", 200, Encoding.ASCII)
            Catch
                ' ignore
            End Try
        End Try
    End Sub

    'Game Handlers
    Private Sub HandleTennisUl(context As HttpListenerContext)
        Dim req = context.Request
        Dim qs = HttpUtility.ParseQueryString(req.Url.Query)

        Dim uid As String = qs("uid")
        If String.IsNullOrWhiteSpace(uid) Then uid = "unknown"

        Dim scRaw As String = qs("sc")
        Dim sc As Integer = 0
        If Not String.IsNullOrWhiteSpace(scRaw) Then Integer.TryParse(scRaw, sc)

        Dim pr As Integer = 0
        Try
            pr = sc \ 10000
        Catch
            pr = 0
        End Try

        Dim respText As String = "0:0"

        Try
            Using conn As New NpgsqlConnection(CONNECTION_STRING)
                conn.Open()
                Dim stored As Integer = UpsertTennisScore(conn, uid, pr)
                Dim rank As Long = GetTennisRank(conn, stored) ' COUNT(score > stored)+1
                respText = $"{stored}:{rank}"
                Console.WriteLine($"Tennis: UID={uid}, SubmittedScore={pr}, StoredScore={stored}, Rank={rank}")
            End Using
        Catch ex As Exception
            Console.WriteLine("HandleTennisUl error: " & ex.Message)
            respText = "0:0"
        End Try

        SendTextResponse(context, respText, 200, Encoding.ASCII)
    End Sub
    Private Sub HandleHorumonUl(context As HttpListenerContext)
        Dim req = context.Request
        Dim qs = HttpUtility.ParseQueryString(req.Url.Query)

        Dim uid As String = qs("uid")
        If String.IsNullOrWhiteSpace(uid) Then uid = "unknown"

        Dim scRaw As String = qs("sc")
        Dim sc As Integer = 0
        If Not String.IsNullOrWhiteSpace(scRaw) Then Integer.TryParse(scRaw, sc)

        Dim respText As String = "0,1"

        Try
            Using conn As New NpgsqlConnection(CONNECTION_STRING)
                conn.Open()
                Dim stored As Integer = UpsertHorumonScore(conn, uid, sc)
                Dim rank As Long = GetHorumonRank(conn, stored)
                respText = $"{rank},0"
                Console.WriteLine($"Horumon: UID={uid}, SubmittedScore={sc}, StoredScore={stored}, Rank={rank}")
            End Using
        Catch ex As Exception
            Console.WriteLine("HandleHorumonUl error: " & ex.Message)
            respText = "0,1"
        End Try

        SendTextResponse(context, respText, 200, Encoding.ASCII)
    End Sub
    Private Sub HandleBlock2Ul(context As HttpListenerContext)
        Dim req = context.Request
        Dim qs = HttpUtility.ParseQueryString(req.Url.Query)

        Dim uid As String = qs("uid")
        If String.IsNullOrWhiteSpace(uid) Then uid = "unknown"

        Dim sc As Integer = 0, sl As Integer = 0, sh As Integer = 0
        Dim scRaw As String = qs("sc")
        Dim slRaw As String = qs("sl")
        Dim shRaw As String = qs("sh")
        If Not String.IsNullOrWhiteSpace(scRaw) Then Integer.TryParse(scRaw, sc)
        If Not String.IsNullOrWhiteSpace(slRaw) Then Integer.TryParse(slRaw, sl)
        If Not String.IsNullOrWhiteSpace(shRaw) Then Integer.TryParse(shRaw, sh)

        Dim respText As String = "0,0,1"

        Try
            Using conn As New NpgsqlConnection(CONNECTION_STRING)
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Try
                        Dim stored As Integer = UpsertBlock2Score(conn, tx, uid, sc, sl, sh)
                        Dim total As Long = GetBlock2Total(conn, tx)
                        Dim rank As Long = GetBlock2Rank(conn, tx, stored)

                        tx.Commit()
                        respText = $"{rank},{total},0"
                        Console.WriteLine($"Block2: UID={uid}, SubmittedScore={sc}, StoredScore={stored}, Rank={rank}, TotalPlayers={total}")
                    Catch ex As Exception
                        Try : tx.Rollback() : Catch : End Try
                        Console.WriteLine("HandleBlock2Ul TX error: " & ex.Message)
                        respText = "0,0,1"
                    End Try
                End Using
            End Using

        Catch ex As Exception
            Console.WriteLine("HandleBlock2Ul error: " & ex.Message)
            respText = "0,0,1"
        End Try

        SendTextResponse(context, respText, 200, Encoding.ASCII)
    End Sub
    Private Sub HandleDaifugouUl(context As HttpListenerContext)
        Dim req = context.Request
        Dim qs = HttpUtility.ParseQueryString(req.Url.Query)

        Dim uid As String = qs("uid")
        If String.IsNullOrWhiteSpace(uid) Then uid = "NULLGWDOCOMO"
        Dim gi As Func(Of String, Integer) =
        Function(k As String)
            Dim raw = qs(k)
            Dim v As Integer = 0
            If Not String.IsNullOrWhiteSpace(raw) Then Integer.TryParse(raw, v)
            Return v
        End Function

        Dim dtRaw As String = qs("dt")
        Dim t_sc As Integer = gi("t_sc")
        Dim m_sc As Integer = gi("m_sc")
        Dim mill As Integer = gi("mill")
        Dim revo As Integer = gi("revo")
        Dim first As Integer = gi("first")
        Dim second As Integer = gi("second")
        Dim third As Integer = gi("third")
        Dim fourth As Integer = gi("fourth")

        Dim dtObj As DateTimeOffset = DateTimeOffset.UtcNow
        If Not String.IsNullOrWhiteSpace(dtRaw) Then
            ' dt format: YYYYMMDDHHMMSS (UTC)
            Dim tmp As DateTime
            If DateTime.TryParseExact(dtRaw, "yyyyMMddHHmmss",
                                  Globalization.CultureInfo.InvariantCulture,
                                  Globalization.DateTimeStyles.AssumeUniversal Or Globalization.DateTimeStyles.AdjustToUniversal,
                                  tmp) Then
                dtObj = New DateTimeOffset(tmp, TimeSpan.Zero)
            End If
        End If

        Dim respText As String = "0:0:0:0:0:0:0:0:1:0"

        Try
            Using conn As New NpgsqlConnection(CONNECTION_STRING)
                conn.Open()
                ' --- Upsert incoming info and read stored fields ---
                Dim stored_t_sc As Integer, stored_m_sc As Integer, stored_mill As Integer, stored_revo As Integer
                Dim stored_first As Integer, stored_second As Integer, stored_third As Integer, stored_fourth As Integer
                Dim stored_last_played As DateTimeOffset? = Nothing
                Dim stored_lastmonth_ym As String = Nothing
                Dim stored_lastmonth_rank As Integer = 0
                Dim stored_bonus_pending As Integer = 0
                Dim stored_bonus_sent As Boolean = False

                Try
                    Using tx = conn.BeginTransaction()
                        Using cmd As New NpgsqlCommand("
                        INSERT INTO daifugou
                          (uid, t_sc, m_sc, mill, revo, first, second, third, fourth, last_played)
                        VALUES
                          (@uid, @t_sc, @m_sc, @mill, @revo, @first, @second, @third, @fourth, @last_played)
                        ON CONFLICT (uid) DO UPDATE
                          SET t_sc = GREATEST(daifugou.t_sc, EXCLUDED.t_sc),
                              m_sc = EXCLUDED.m_sc,
                              mill = GREATEST(daifugou.mill, EXCLUDED.mill),
                              revo = GREATEST(daifugou.revo, EXCLUDED.revo),
                              first = GREATEST(daifugou.first, EXCLUDED.first),
                              second = GREATEST(daifugou.second, EXCLUDED.second),
                              third = GREATEST(daifugou.third, EXCLUDED.third),
                              fourth = GREATEST(daifugou.fourth, EXCLUDED.fourth),
                              last_played = CASE
                                WHEN daifugou.last_played IS NULL THEN EXCLUDED.last_played
                                WHEN EXCLUDED.last_played IS NULL THEN daifugou.last_played
                                WHEN EXCLUDED.last_played > daifugou.last_played THEN EXCLUDED.last_played
                                ELSE daifugou.last_played
                              END
                        RETURNING
                          t_sc, m_sc, mill, revo, first, second, third, fourth,
                          last_played, lastmonth_ym, lastmonth_rank, bonus_pending, bonus_sent;
                    ", conn, tx)

                            cmd.Parameters.AddWithValue("uid", uid)
                            cmd.Parameters.AddWithValue("t_sc", t_sc)
                            cmd.Parameters.AddWithValue("m_sc", m_sc)
                            cmd.Parameters.AddWithValue("mill", mill)
                            cmd.Parameters.AddWithValue("revo", revo)
                            cmd.Parameters.AddWithValue("first", first)
                            cmd.Parameters.AddWithValue("second", second)
                            cmd.Parameters.AddWithValue("third", third)
                            cmd.Parameters.AddWithValue("fourth", fourth)
                            cmd.Parameters.AddWithValue("last_played", dtObj)

                            Using r = cmd.ExecuteReader()
                                If Not r.Read() Then Throw New Exception("No RETURNING row")
                                stored_t_sc = r.GetInt32(0)
                                stored_m_sc = r.GetInt32(1)
                                stored_mill = r.GetInt32(2)
                                stored_revo = r.GetInt32(3)
                                stored_first = r.GetInt32(4)
                                stored_second = r.GetInt32(5)
                                stored_third = r.GetInt32(6)
                                stored_fourth = r.GetInt32(7)

                                If Not r.IsDBNull(8) Then stored_last_played = r.GetFieldValue(Of DateTimeOffset)(8)
                                If Not r.IsDBNull(9) Then stored_lastmonth_ym = r.GetString(9)
                                If Not r.IsDBNull(10) Then stored_lastmonth_rank = r.GetInt32(10)
                                If Not r.IsDBNull(11) Then stored_bonus_pending = r.GetInt32(11)
                                If Not r.IsDBNull(12) Then stored_bonus_sent = r.GetBoolean(12)
                            End Using
                        End Using
                        tx.Commit()
                        Console.WriteLine($"Daifugou: UID={uid}, SubmittedScores=[t_sc={t_sc}, m_sc={m_sc}, mill={mill}, revo={revo}], StoredScores=[t_sc={stored_t_sc}, m_sc={stored_m_sc}, mill={stored_mill}, revo={stored_revo}]")
                    End Using
                Catch
                    Console.WriteLine("HandleDaifugouUl upsert error")
                    SendTextResponse(context, respText, 200, Encoding.ASCII)
                    Return
                End Try

                ' --- Month-end compute (silent) ---
                Try
                    ComputeDaifugouMonthEndIfNeeded(conn, DateTimeOffset.UtcNow)
                Catch
                    ' swallow like Python
                End Try

                ' --- Claim bonus atomically if eligible ---
                Dim bonusToSend As Integer = 0
                Dim lastmonthYmField As String = Nothing
                Dim lastmonthRankField As Integer? = Nothing

                Try
                    Using tx = conn.BeginTransaction()
                        Try
                            Using cmdSel As New NpgsqlCommand("
                SELECT bonus_pending, bonus_sent, lastmonth_ym, lastmonth_rank
                FROM daifugou
                WHERE uid = @uid
                FOR UPDATE;
            ", conn, tx)
                                cmdSel.Parameters.AddWithValue("uid", uid)

                                Using r = cmdSel.ExecuteReader()
                                    If r.Read() Then
                                        Dim bp As Integer = If(r.IsDBNull(0), 0, r.GetInt32(0))
                                        Dim bsent As Boolean = If(r.IsDBNull(1), False, r.GetBoolean(1))
                                        lastmonthYmField = If(r.IsDBNull(2), Nothing, r.GetString(2))
                                        lastmonthRankField = If(r.IsDBNull(3), 0, r.GetInt32(3))

                                        If bp <> 0 AndAlso Not bsent Then
                                            bonusToSend = bp
                                        End If
                                    End If
                                End Using
                            End Using

                            If bonusToSend <> 0 Then
                                Using cmdUpd As New NpgsqlCommand("
                    UPDATE daifugou
                       SET bonus_pending = 0,
                           bonus_sent = TRUE
                     WHERE uid = @uid;
                ", conn, tx)
                                    cmdUpd.Parameters.AddWithValue("uid", uid)
                                    cmdUpd.ExecuteNonQuery()
                                End Using
                            End If

                            tx.Commit()

                        Catch
                            Try : tx.Rollback() : Catch : End Try
                            bonusToSend = 0
                        End Try
                    End Using

                Catch
                    bonusToSend = 0
                End Try

                ' --- Pop & ranks ---
                Dim population As Long = 0
                Dim populationMNonzero As Long = 0
                Dim rankTotal As Long = 0
                Dim rankMonth As Long = 0
                Dim rankMill As Long = 0
                Dim rankRevo As Long = 0

                Try
                    Using cmdPop As New NpgsqlCommand("SELECT COUNT(*) FROM daifugou;", conn)
                        population = Convert.ToInt64(cmdPop.ExecuteScalar())
                    End Using

                    Using cmdPopM As New NpgsqlCommand("SELECT COUNT(*) FROM daifugou WHERE m_sc <> 0;", conn)
                        populationMNonzero = Convert.ToInt64(cmdPopM.ExecuteScalar())
                    End Using

                    Dim rankOf As Func(Of String, Integer, Long) =
                    Function(col As String, val As Integer)
                        Using cmd As New NpgsqlCommand($"SELECT COUNT(*) FROM daifugou WHERE {col} > @v;", conn)
                            cmd.Parameters.AddWithValue("v", val)
                            Return Convert.ToInt64(cmd.ExecuteScalar()) + 1
                        End Using
                    End Function

                    rankTotal = rankOf("t_sc", stored_t_sc)

                    If stored_m_sc = 0 Then
                        rankMonth = 0
                    Else
                        If populationMNonzero = 0 Then populationMNonzero = 1 ' match Python defensive behavior
                        rankMonth = rankOf("m_sc", stored_m_sc)
                    End If

                    rankMill = rankOf("mill", stored_mill)
                    rankRevo = rankOf("revo", stored_revo)

                Catch
                    respText = "0:0:0:0:0:0:0:0:1:0"
                    Console.WriteLine("HandleDaifugouUl rank/pop error")
                    SendTextResponse(context, respText, 200, Encoding.ASCII)
                    Return
                End Try

                Dim lastmonthYmOut As Object = If(String.IsNullOrWhiteSpace(lastmonthYmField), 0, lastmonthYmField)
                Dim lastmonthRankOut As Integer = If(lastmonthRankField, 0)

                Dim fields As String() = {
                CInt(population).ToString(),
                CInt(rankTotal).ToString(),
                CInt(rankMonth).ToString(),
                lastmonthYmOut.ToString(),
                CInt(lastmonthRankOut).ToString(),
                CInt(bonusToSend).ToString(),
                CInt(rankMill).ToString(),
                CInt(rankRevo).ToString(),
                "0",
                CInt(populationMNonzero).ToString()
            }

                respText = String.Join(":", fields)
            End Using

        Catch ex As Exception
            Console.WriteLine("HandleDaifugouUl error: " & ex.Message)
            respText = "0:0:0:0:0:0:0:0:1:0"
        End Try
        Console.WriteLine($"Daifugou Response: {respText}")
        SendTextResponse(context, respText, 200, Encoding.ASCII)
    End Sub

    'Daifugou Helpers
    Private Function PrevMonthYm(nowUtc As DateTimeOffset) As String
        Dim y = nowUtc.Year
        Dim m = nowUtc.Month - 1
        If m = 0 Then
            m = 12
            y -= 1
        End If
        Return $"{y:0000}{m:00}"
    End Function
    Private Sub MonthBoundsFromYm(monthYm As String, ByRef startUtc As DateTimeOffset, ByRef endUtc As DateTimeOffset)
        Dim year As Integer = Integer.Parse(monthYm.Substring(0, 4))
        Dim month As Integer = Integer.Parse(monthYm.Substring(4, 2))
        Dim startDt As New DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)
        Dim endDt As DateTime
        If month = 12 Then
            endDt = New DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        Else
            endDt = New DateTime(year, month + 1, 1, 0, 0, 0, DateTimeKind.Utc)
        End If
        startUtc = New DateTimeOffset(startDt)
        endUtc = New DateTimeOffset(endDt)
    End Sub
    Private Function ComputeDaifugouMonthEndIfNeeded(conn As NpgsqlConnection, nowUtc As DateTimeOffset) As Boolean
    Dim targetYm = PrevMonthYm(nowUtc)

    ' Quick check: already computed?
    Using cmdQuick As New NpgsqlCommand("SELECT 1 FROM daifugou_monthly_meta WHERE month_ym = @ym LIMIT 1;", conn)
        cmdQuick.Parameters.AddWithValue("ym", targetYm)
        Dim already = cmdQuick.ExecuteScalar()
        If already IsNot Nothing Then Return False
    End Using

        Dim ms As DateTimeOffset, mi As DateTimeOffset
        MonthBoundsFromYm(targetYm, ms, mi)

        ' ensure now is after month end
        If nowUtc < mi Then Return False

        Using tx = conn.BeginTransaction()
        Try
            ' lock meta table to prevent races (matches Python idea)
            Using cmdLock As New NpgsqlCommand("LOCK TABLE daifugou_monthly_meta IN SHARE ROW EXCLUSIVE MODE;", conn, tx)
                cmdLock.ExecuteNonQuery()
            End Using

            ' re-check under lock
            Using cmdChk As New NpgsqlCommand("SELECT 1 FROM daifugou_monthly_meta WHERE month_ym = @ym LIMIT 1;", conn, tx)
                cmdChk.Parameters.AddWithValue("ym", targetYm)
                Dim already = cmdChk.ExecuteScalar()
                If already IsNot Nothing Then
                    tx.Commit()
                    Return False
                End If
            End Using

            ' top players in target month by m_sc (ties stable by uid asc)
            Dim rows As New List(Of Tuple(Of String, Integer))()
            Using cmdTop As New NpgsqlCommand("
                SELECT uid, m_sc
                FROM daifugou
                WHERE last_played >= @s AND last_played < @e
                ORDER BY m_sc DESC, uid ASC;
            ", conn, tx)
                cmdTop.Parameters.AddWithValue("s", ms)
                    cmdTop.Parameters.AddWithValue("e", mi)
                    Using r = cmdTop.ExecuteReader()
                    While r.Read()
                        Dim u = r.GetString(0)
                        Dim msc = If(r.IsDBNull(1), 0, r.GetInt32(1))
                        rows.Add(Tuple.Create(u, msc))
                    End While
                End Using
            End Using

            ' Update top 3: bonus = m_sc // (2*rank) if m_sc>0 else 0
            For i As Integer = 0 To Math.Min(2, rows.Count - 1)
                Dim rank = i + 1
                Dim u = rows(i).Item1
                Dim msc = rows(i).Item2
                Dim bonus As Integer = If(msc > 0, msc \ (2 * rank), 0)

                Using cmdUpd As New NpgsqlCommand("
                    UPDATE daifugou
                       SET lastmonth_ym = @ym,
                           lastmonth_rank = @rank,
                           bonus_pending = @bonus,
                           bonus_sent = FALSE
                     WHERE uid = @uid;
                ", conn, tx)
                    cmdUpd.Parameters.AddWithValue("ym", targetYm)
                    cmdUpd.Parameters.AddWithValue("rank", rank)
                    cmdUpd.Parameters.AddWithValue("bonus", bonus)
                    cmdUpd.Parameters.AddWithValue("uid", u)
                    cmdUpd.ExecuteNonQuery()
                End Using
            Next

            ' zero monthly scores for everybody
            Using cmdZero As New NpgsqlCommand("UPDATE daifugou SET m_sc = 0 WHERE m_sc <> 0;", conn, tx)
                cmdZero.ExecuteNonQuery()
            End Using

            ' meta insert
            Using cmdMeta As New NpgsqlCommand("
                INSERT INTO daifugou_monthly_meta(month_ym, computed_at)
                VALUES (@ym, now());
            ", conn, tx)
                cmdMeta.Parameters.AddWithValue("ym", targetYm)
                cmdMeta.ExecuteNonQuery()
            End Using

            tx.Commit()
            Return True

        Catch
            Try : tx.Rollback() : Catch : End Try
            Return False
        End Try
    End Using
End Function


    'DB Handlers
    Private Sub EnsureTables()
        Using conn As New NpgsqlConnection(CONNECTION_STRING)
            conn.Open()
            EnsureTennisTable(conn)
            EnsureHorumonTable(conn)
            EnsureBlock2Table(conn)
            EnsureDaifugouTables(conn)
        End Using
    End Sub
    Private Sub EnsureTennisTable(conn As NpgsqlConnection)
        Using cmd As New NpgsqlCommand("
            CREATE TABLE IF NOT EXISTS sonictennis (
                uid   TEXT PRIMARY KEY,
                score INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_sonictennis_score ON sonictennis(score DESC);
        ", conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub
    Private Function UpsertTennisScore(conn As NpgsqlConnection, uid As String, score As Integer) As Integer
        Using cmd As New NpgsqlCommand("
            INSERT INTO sonictennis(uid, score)
            VALUES (@uid, @score)
            ON CONFLICT (uid) DO UPDATE
              SET score = GREATEST(sonictennis.score, EXCLUDED.score)
            RETURNING score;
        ", conn)
            cmd.Parameters.AddWithValue("uid", uid)
            cmd.Parameters.AddWithValue("score", score)
            Return Convert.ToInt32(cmd.ExecuteScalar())
        End Using
    End Function
    Private Function GetTennisRank(conn As NpgsqlConnection, storedScore As Integer) As Long
        Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM sonictennis WHERE score > @s;", conn)
            cmd.Parameters.AddWithValue("s", storedScore)
            Dim higher As Long = Convert.ToInt64(cmd.ExecuteScalar())
            Return higher + 1
        End Using
    End Function
    Private Sub EnsureHorumonTable(conn As NpgsqlConnection)
        Using cmd As New NpgsqlCommand("
        CREATE TABLE IF NOT EXISTS horumon (
            uid   TEXT PRIMARY KEY,
            score INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_horumon_score ON horumon(score DESC);
    ", conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub
    Private Function UpsertHorumonScore(conn As NpgsqlConnection, uid As String, score As Integer) As Integer
        Using cmd As New NpgsqlCommand("
        INSERT INTO horumon(uid, score)
        VALUES (@uid, @score)
        ON CONFLICT (uid) DO UPDATE
          SET score = GREATEST(horumon.score, EXCLUDED.score)
        RETURNING score;
    ", conn)
            cmd.Parameters.AddWithValue("uid", uid)
            cmd.Parameters.AddWithValue("score", score)
            Return Convert.ToInt32(cmd.ExecuteScalar())
        End Using
    End Function
    Private Function GetHorumonRank(conn As NpgsqlConnection, storedScore As Integer) As Long
        Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM horumon WHERE score > @s;", conn)
            cmd.Parameters.AddWithValue("s", storedScore)
            Dim higher As Long = Convert.ToInt64(cmd.ExecuteScalar())
            Return higher + 1
        End Using
    End Function
    Private Sub EnsureBlock2Table(conn As NpgsqlConnection)
        Using cmd As New NpgsqlCommand("
        CREATE TABLE IF NOT EXISTS block2 (
            uid   TEXT PRIMARY KEY,
            score INTEGER NOT NULL,
            sl    INTEGER,
            sh    INTEGER
        );
        CREATE INDEX IF NOT EXISTS idx_block2_score ON block2(score DESC);
    ", conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub
    Private Function UpsertBlock2Score(conn As NpgsqlConnection, tx As NpgsqlTransaction, uid As String, score As Integer, sl As Integer, sh As Integer) As Integer
        Using cmd As New NpgsqlCommand("
        INSERT INTO block2 (uid, score, sl, sh)
        VALUES (@uid, @score, @sl, @sh)
        ON CONFLICT (uid) DO UPDATE
          SET score = GREATEST(block2.score, EXCLUDED.score),
              sl = EXCLUDED.sl,
              sh = EXCLUDED.sh
        RETURNING score;
    ", conn, tx)
            cmd.Parameters.AddWithValue("uid", uid)
            cmd.Parameters.AddWithValue("score", score)
            cmd.Parameters.AddWithValue("sl", sl)
            cmd.Parameters.AddWithValue("sh", sh)
            Return Convert.ToInt32(cmd.ExecuteScalar())
        End Using
    End Function
    Private Function GetBlock2Total(conn As NpgsqlConnection, tx As NpgsqlTransaction) As Long
        Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM block2;", conn, tx)
            Return Convert.ToInt64(cmd.ExecuteScalar())
        End Using
    End Function
    Private Function GetBlock2Rank(conn As NpgsqlConnection, tx As NpgsqlTransaction, storedScore As Integer) As Long
        Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM block2 WHERE score > @s;", conn, tx)
            cmd.Parameters.AddWithValue("s", storedScore)
            Dim higher As Long = Convert.ToInt64(cmd.ExecuteScalar())
            Return higher + 1
        End Using
    End Function
    Private Sub EnsureDaifugouTables(conn As NpgsqlConnection)
        Using cmd As New NpgsqlCommand("
        CREATE TABLE IF NOT EXISTS daifugou (
            uid TEXT PRIMARY KEY,
            t_sc INTEGER DEFAULT 0,
            m_sc INTEGER DEFAULT 0,
            mill INTEGER DEFAULT 0,
            revo INTEGER DEFAULT 0,
            first INTEGER DEFAULT 0,
            second INTEGER DEFAULT 0,
            third INTEGER DEFAULT 0,
            fourth INTEGER DEFAULT 0,
            last_played TIMESTAMPTZ,
            lastmonth_ym TEXT,
            lastmonth_rank INTEGER DEFAULT 0,
            bonus_pending INTEGER DEFAULT 0,
            bonus_sent BOOLEAN DEFAULT FALSE
        );

        CREATE TABLE IF NOT EXISTS daifugou_monthly_meta (
            month_ym TEXT PRIMARY KEY,
            computed_at TIMESTAMPTZ NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_daifugou_t_sc ON daifugou(t_sc DESC);
        CREATE INDEX IF NOT EXISTS idx_daifugou_m_sc ON daifugou(m_sc DESC);
        CREATE INDEX IF NOT EXISTS idx_daifugou_last_played ON daifugou(last_played);
    ", conn)
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    ' Utilities
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
    Private Sub SendTextResponse(context As HttpListenerContext, body As String, statusCode As Integer, enc As Encoding)
        Dim resp = context.Response
        Dim data = enc.GetBytes(body)

        resp.StatusCode = statusCode
        resp.ContentType = "text/plain"
        resp.ContentEncoding = enc
        resp.ContentLength64 = data.Length

        Using os = resp.OutputStream
            os.Write(data, 0, data.Length)
        End Using
    End Sub
End Module