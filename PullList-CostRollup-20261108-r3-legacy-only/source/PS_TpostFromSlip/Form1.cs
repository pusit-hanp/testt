using PS_TpostFromSlip.BizModels;
using SAP.Middleware.Connector;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;


namespace PS_TpostFromSlip
{
    public partial class Form1 : Form
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private string CurrentStatus;
        private string InprocStatus;
        private string SAPDocField;
        private string MoveMentType;
        private string FilterMaterial;
        private string ChangeBatch;
        private string BatchName;
        private string MatGroup;
        private string SQLCondition;
        private string FSLocField;
        private string TSLocField;
        private string SapName;
        private string SapLanguage;
        private string SapAppServerHost;
        private string SapSystemNumber;
        private string SapClient;
        private string SapUsername;
        private string SapPassword;
        private string ConfirmCtn;
        private bool EnableToSendEntryUOMToSAP;

        private string SAPUrl;
        private string ClientSecretKey;
        private string ClientId;
        private bool EnableAPIToSAP;

        log4net.ILog log;


        private string ConnectionString = "";

        public Form1()
        {
            InitializeComponent();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {

            label1.Text = "";
            string user = "";
            string pass = "";

            //Load config
            log4net.Config.XmlConfigurator.Configure();
            log = log4net.LogManager.GetLogger(typeof(Form1));
            log.Info("service start !!");

            GlobalBiziness.SECrypt ec = new GlobalBiziness.SECrypt();
            user = PS_TpostFromSlip.Properties.Settings.Default.UserName.Trim();
            pass = PS_TpostFromSlip.Properties.Settings.Default.Password.Trim();

            //    ConnectionString = PS_TpostFromSlip.Properties.Settings.Default.DBConnection + "User Id = " + user + "; Password = " + ec.Decrypt(pass) + ";";
            ConnectionString = PS_TpostFromSlip.Properties.Settings.Default.DBConnection + "User Id = " + user + "; Password = " + ec.Decrypt(pass) + ";";

            SapName = PS_TpostFromSlip.Properties.Settings.Default.SapName;
            SapLanguage = PS_TpostFromSlip.Properties.Settings.Default.SapLanguage;
            SapAppServerHost = PS_TpostFromSlip.Properties.Settings.Default.SapAppServerHost;
            SapSystemNumber = PS_TpostFromSlip.Properties.Settings.Default.SapSystemNumber;
            SapClient = PS_TpostFromSlip.Properties.Settings.Default.SapClient;
            SapUsername = PS_TpostFromSlip.Properties.Settings.Default.SapUsername;
            SapPassword = ec.Decrypt(PS_TpostFromSlip.Properties.Settings.Default.SapPassword);

            CurrentStatus = PS_TpostFromSlip.Properties.Settings.Default.CurrentStatus;
            InprocStatus = PS_TpostFromSlip.Properties.Settings.Default.InprocStatus;
            SAPDocField = PS_TpostFromSlip.Properties.Settings.Default.SAPDocField;
            MoveMentType = PS_TpostFromSlip.Properties.Settings.Default.MoveMentType;
            FilterMaterial = PS_TpostFromSlip.Properties.Settings.Default.FilterMaterial;
            ChangeBatch = PS_TpostFromSlip.Properties.Settings.Default.ChangeBatch;
            BatchName = PS_TpostFromSlip.Properties.Settings.Default.BatchName;
            MatGroup = PS_TpostFromSlip.Properties.Settings.Default.MatGroup;
            FSLocField = PS_TpostFromSlip.Properties.Settings.Default.FSLocField;
            TSLocField = PS_TpostFromSlip.Properties.Settings.Default.TSLocField;
            SQLCondition = PS_TpostFromSlip.Properties.Settings.Default.SQLCondition;
            ConfirmCtn = PS_TpostFromSlip.Properties.Settings.Default.ConfirmCtn;
            EnableToSendEntryUOMToSAP = PS_TpostFromSlip.Properties.Settings.Default.EnableToSendEntryUOMToSAP;

            SAPUrl = PS_TpostFromSlip.Properties.Settings.Default.SAPUrl;
            ClientSecretKey = PS_TpostFromSlip.Properties.Settings.Default.ClientSecretKey;
            ClientId = PS_TpostFromSlip.Properties.Settings.Default.ClientId;
            EnableAPIToSAP = PS_TpostFromSlip.Properties.Settings.Default.EnableAPIToSAP;

            if (PS_TpostFromSlip.Properties.Settings.Default.AutoRun)
            {
                this.Hide();
                await ProcessRun();
                Application.Exit();
                this.Close();
            }

        }

        private async void btnProcess_Click(object sender, EventArgs e)
        {
            if (processRunActive) return;
            try
            {
                await ProcessRun();
                Application.Exit();
                this.Close();
            }
            catch (Exception ex)
            {
                log.Error("Error Step-1 : " + ex.Message.ToString());
            }
        }

        private async Task ProcessRun()
        {
            if (processRunActive) return;
            processRunActive = true;
            try
            {
                await ProcessRunCore();
            }
            finally
            {
                processRunActive = false;
            }
        }

        private async Task ProcessRunCore()
        {
            const string func = "ProcessRun";
            // Capture Cost Rollup state when processing starts, not when the UI loads.
            // A fresh guard per run makes admin/time changes effective next run.
            try
            {
                InitializeCostRollup();
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                log.Error("Cost Rollup startup failed; no posting will run.", ex);
                label1.Text = "Cost Rollup startup failed. See log.";
                return;
            }
            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();

            try
            {
                label1.Text = "On Process...";
                RfcConfigParameters config = new RfcConfigParameters
                {
                    { RfcConfigParameters.Name, SapName },
                    { RfcConfigParameters.Language, SapLanguage },
                    { RfcConfigParameters.AppServerHost, SapAppServerHost },
                    { RfcConfigParameters.SystemNumber, SapSystemNumber },
                    { RfcConfigParameters.Client, SapClient },
                    { RfcConfigParameters.User, SapUsername },
                    { RfcConfigParameters.Password, SapPassword }
                };
                RfcDestination neurfc = RfcDestinationManager.GetDestination(config);
                bool sap_maintenance = false;
                if (neurfc != null)
                {
                    var linkconn = new MyRepository.Invoke(ConnectionString);

                    string StrBdSql = "SELECT FROM_DATE, TO_DATE FROM PULLLIST_SAP_MAINTENANCE ";
                    var data = linkconn.Get<PULLLIST_SAP_MAINTENANCE>(StrBdSql);
                    if (data.Count > 0)
                    {
                        DateTime now_date = DateTime.Now;
                        DateTime from_date = Convert.ToDateTime(data[0].FROM_DATE);
                        DateTime to_date = Convert.ToDateTime(data[0].TO_DATE);
                        if (now_date >= from_date && now_date <= to_date)
                        {
                            sap_maintenance = true;
                        }
                    }

                    if (!sap_maintenance)
                    {
                        GetAndUpdateStatusMiSlipNoInPostError(this.CurrentStatus, this.InprocStatus);
                        ConfirmMovement(this.MoveMentType, this.CurrentStatus, this.InprocStatus, this.SAPDocField, neurfc);
                        await PostingMaterial(this.MoveMentType, this.CurrentStatus, this.InprocStatus, this.SAPDocField, neurfc);
                        label1.Text = "Done...";
                    }
                }

                label1.Text = "No Update...";
            }
            catch (Exception ex)
            {
                //End stop watch
                stopWatch.Stop();
                WriteLogStopwatchError(stopWatch, string.Format("{0}:{1}", func, ex.Message));

                label1.Text = string.Format("{0} : {1}", func, ex.Message.ToString());
                log.Error("ProcessRun : " + ex.Message);
            }

            //End stop watch
            stopWatch.Stop();
            WriteLogStopwatchInfo(stopWatch, string.Format("{0} Done", func));
        }

        private void GetAndUpdateStatusMiSlipNoInPostError(string currentStatus, string inProcStatus)
        {
            const string func = "GetAndUpdateStatusMiSlipNoInPostError";
            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();

            var linkconn = new MyRepository.Invoke(ConnectionString);
            string sql = "UPDATE TPHEADER SET STATUS = curStat " +
                "          WHERE STATUS = inpStat " +
                "          AND MISLIP_NO in (SELECT MISLIP_NO FROM POST_ERROR )" +
                "          AND ((POST_DATE IS NOT NULL) OR (POST_DATE IS NULL AND RECEIVE_DATE < (SYSDATE - (100/1440))))";

            linkconn.parameters.Clear();
            linkconn.parameters.Add(new KeyValuePair<string, dynamic>("curStat", CurrentStatus));
            linkconn.parameters.Add(new KeyValuePair<string, dynamic>("inpStat", InprocStatus));
            linkconn.ExecuteNow(sql);

            //End stop watch
            stopWatch.Stop();
            WriteLogStopwatchInfo(stopWatch, func);
        }

        private void ConfirmMovement(string mvtType, string curStat, string inpStat, string dbField, RfcDestination sapConn)
        {
            const string func = "ConfirmMovement";

            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();
            var linkconn = new MyRepository.Invoke(ConnectionString);

            List<string> slipsToPostWip = new List<string>();
            List<string> slipsToStatus13 = new List<string>();
            string strMiSlip = "";
            try
            {
                string filterSql = string.IsNullOrEmpty(FilterMaterial) ? "" : $" AND SUBSTR(MISLIP_NO,0,2) IN ({FilterMaterial})";
                string sqlQuery = $@"SELECT MISLIP_NO, {dbField} AS SAPDOC, STATUS, NVL(POST_NO,0) AS POST_NO
                                    FROM TPHEADER
                                    WHERE STATUS = curStat AND {dbField} IS NOT NULL {filterSql}
                                    ORDER BY MISLIP_NO";

                linkconn.parameters.Clear();
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("curStat", Convert.ToInt32(curStat)));

                List<tpheader> datas = linkconn.Get<tpheader>(sqlQuery);
                log.Info($"{func}: Found {datas.Count} records to process.");

                //start stop watch foreach
                Stopwatch stopWatch_foreach = Stopwatch.StartNew();

                foreach (var item in datas)
                {
                    //start stop watch data
                    Stopwatch stopWatch_data = Stopwatch.StartNew();

                    strMiSlip = item.mislip_no;
                    int numCount = Convert.ToInt32(item.post_no);
                    strMiSlip = item.mislip_no;

                    #region Update status is inprocess
                    if (!(curStat == "10" && !string.IsNullOrEmpty(item.sapdoc)))
                    {
                        linkconn.parameters.Clear();
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("inpStat", Convert.ToInt32(inpStat)));
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", strMiSlip));
                        linkconn.ExecuteNow("UPDATE TPHEADER SET STATUS = inpStat WHERE MISLIP_NO = miSlipNo");
                    }
                    #endregion

                    if (!string.IsNullOrEmpty(item.sapdoc))
                    {
                        BizClass.BizSAP bbs = new BizClass.BizSAP();

                        if (bbs.ChkdocSAP(item.sapdoc, sapConn))
                        {
                            if (mvtType == "313") slipsToStatus13.Add(strMiSlip);
                            else slipsToPostWip.Add(strMiSlip);

                            if (numCount > 0)
                            {
                                linkconn.parameters.Clear();
                                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", strMiSlip));
                                linkconn.ExecuteNow($"UPDATE TPHEADER SET POST_NO = 0 WHERE MISLIP_NO = miSlipNo");
                            }
                        }
                        else
                        {
                            if (numCount > 50)
                            {
                                linkconn.parameters.Clear();
                                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", strMiSlip));
                                linkconn.ExecuteNow($"DELETE SLIP_DOCUMENT WHERE MISLIP_NO = miSlipNo");

                                string moveSql = $"INSERT INTO SLIP_DOCUMENT (MISLIP_NO, SAP_DOC, POST_DATE) SELECT MISLIP_NO, {dbField}, POST_DATE ";
                                moveSql += $"FROM TPHEADER WHERE MISLIP_NO = miSlipNo ";
                                linkconn.ExecuteNow(moveSql);

                                //Update TPHEADER
                                linkconn.ExecuteNow($"UPDATE TPHEADER SET {dbField} = null , POST_NO = 0 WHERE MISLIP_NO = miSlipNo");
                            }
                            else
                            {
                                linkconn.ExecuteNow($"UPDATE TPHEADER SET POST_NO = {numCount + 1} WHERE MISLIP_NO = miSlipNo");
                            }
                        }
                    }

                    #region Update status is Current
                    if (!(curStat == "10" && !string.IsNullOrEmpty(item.sapdoc)))
                    {
                        linkconn.parameters.Clear();
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("curStat", Convert.ToInt32(curStat)));
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", strMiSlip));
                        linkconn.ExecuteNow("UPDATE TPHEADER SET STATUS = curStat WHERE MISLIP_NO = miSlipNo");
                    }
                    #endregion

                    //End stop watch data
                    stopWatch_data.Stop();
                    WriteLogStopwatchInfo(stopWatch_data, string.Format("{0}: MiSlipNo {1}", func, item.mislip_no));
                }

                // Post WIP
                if (slipsToPostWip.Any())
                {
                    var swWip = Stopwatch.StartNew();
                    foreach (var slip in slipsToPostWip)
                    {
                        try
                        {
                            linkconn.parameters.Clear();
                            linkconn.parameters.Add(new KeyValuePair<string, dynamic>("v_mi_slip", slip));
                            linkconn.Exec("POST_WIP");
                        }
                        catch (Exception ex) { log.Error($"{func} POST_WIP Error [{slip}]: {ex.Message}"); }
                    }
                    WriteLogStopwatchInfo(swWip, $"{func}: Total POST_WIP {slipsToPostWip.Count}");
                }

                // Process Update Status = 13
                if (slipsToStatus13.Any())
                {
                    var swStatus = Stopwatch.StartNew();
                    // แบ่งกลุ่มอัปเดตทีละ 50 รายการเพื่อป้องกัน SQL ยาวเกินไป
                    for (int i = 0; i < slipsToStatus13.Count; i += 50)
                    {
                        var take50Slips = slipsToStatus13.Skip(i).Take(50);
                        string inClause = string.Join("','", take50Slips);
                        linkconn.ExecuteNow($"UPDATE TPHEADER SET STATUS = 13 WHERE MISLIP_NO IN ('{inClause}')");
                    }
                    WriteLogStopwatchInfo(swStatus, $"{func}: Total Status 13 {slipsToStatus13.Count}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"{func}: MiSlipNo {strMiSlip} => {ex.Message}");
            }
            finally
            {
                //End stop watch
                stopWatch.Stop();
                WriteLogStopwatchInfo(stopWatch, $"{func}: Overall Execution Time");
            }
        }

        private async Task PostingMaterial(string mvtType, string curStat, string inpStat, string dbField, RfcDestination sapCon)
        {
            const string func = "PostingMaterial";
            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();

            var linkconn = new MyRepository.Invoke(ConnectionString);
            List<string> processedMiSlips = new List<string>();

            try
            {
                string filterSql = string.IsNullOrEmpty(FilterMaterial) ? "" : $" AND SUBSTR(MISLIP_NO,0,2) IN ({FilterMaterial})";
                string querySql = $@" SELECT MISLIP_NO, STATUS, ISSUE_BY
                                     FROM TPHEADER
                                     WHERE STATUS = curStat
                                       AND {SQLCondition}
                                       {filterSql}
                                       AND DOC_DATE > SYSDATE - 30
                                     ORDER BY MISLIP_NO";

                linkconn.parameters.Clear();
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("curStat", Convert.ToInt32(curStat)));
                var data = linkconn.Get<tpheader>(querySql);

                foreach (var item in data)
                {
                    // Check before touching TPHEADER or clearing POST_ERROR.
                    if (ShouldSkipForCostRollup(item.mislip_no)) continue;
                    processedMiSlips.Add(item.mislip_no);

                    try
                    {
                        //Update status is inprocess
                        linkconn.parameters.Clear();
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("inpStat", Convert.ToInt32(inpStat)));
                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", item.mislip_no));
                        linkconn.ExecuteNow($"UPDATE TPHEADER SET STATUS = inpStat WHERE MISLIP_NO = miSlipNo");

                        // เช็ค Material Group (cstRoll)
                        if (MatGroup.IndexOf(item.mislip_no.Substring(3, 2)) != -1) continue;

                        DateTime now = DateTime.Now;
                        string strPrdDate = now.ToString("yyyyMMdd");
                        string strExpDate = now.AddMonths(4).ToString("yyyyMMdd");
                        string batNameParam = (ChangeBatch == "Y") ? BatchName : "batch";

                        string prefix = item.mislip_no.Trim().Substring(0, 2).ToUpper();
                        if (prefix == "RE" || prefix == "SH") batNameParam = "to_batch";

                        string detailSql = BuildSql(mvtType, strPrdDate, strExpDate, batNameParam, item.mislip_no);
                        List<data_header> headerDetails = linkconn.Get<data_header>(detailSql);

                        if (headerDetails.Any())
                        {
                            //Delete MISLIP_NO in POST_ERROR
                            linkconn.parameters.Clear();
                            linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", item.mislip_no));
                            linkconn.ExecuteNow($"DELETE FROM POST_ERROR WHERE MISLIP_NO = miSlipNo");

                            data_header master = headerDetails.First();
                            string strDocDate = !string.IsNullOrEmpty(master.doc_date?.ToString()) ? master.xdoc_date.ToString() : strPrdDate;
                            string strPostDate = strPrdDate;

                            if (sapCon != null)
                            {
                                BizClass.BizSAP sapBiz = new BizClass.BizSAP();
                                string pureMiSlip = item.mislip_no.Split('_')[0];

                                // SAP Transfer Posting
                                bool isSuccess = false;
                                string sapDocumentNumber = "";
                                string sapErrorMessage = "";

                                if (EnableAPIToSAP)
                                {
                                    TranferToSAPModel result = await TranferToSAPAsync(pureMiSlip, strPostDate, strDocDate, headerDetails);
                                    isSuccess = result.IsSuccess;

                                    if (isSuccess)
                                        sapDocumentNumber = result.MatDoc;
                                    else
                                        sapErrorMessage = result.Message;
                                }
                                else
                                {
                                    string sapMesg = "";
                                    isSuccess = sapBiz.TransferPosting(pureMiSlip, dbField, strPostDate, strDocDate, headerDetails, EnableToSendEntryUOMToSAP, false, ref sapMesg, ref sapCon);

                                    if (isSuccess)
                                        sapDocumentNumber = sapMesg.Trim();
                                    else
                                        sapErrorMessage = sapMesg.Trim();
                                }

                                if (isSuccess)
                                {
                                    linkconn.parameters.Clear();
                                    linkconn.parameters.Add(new KeyValuePair<string, dynamic>("matDoc", sapDocumentNumber));
                                    linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", item.mislip_no));

                                    // FIXED: Removed the stray single quote at the end
                                    linkconn.ExecuteNow($"UPDATE TPHEADER SET {dbField} = matDoc, POST_DATE = SYSDATE WHERE MISLIP_NO = miSlipNo");

                                    if (mvtType == "313" || mvtType == "311")
                                    {
                                        await TranferToSmartAsync(item.issue_by, item.mislip_no);
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        // Insert data to POST_ERROR (existing behavior).
                                        linkconn.parameters.Clear();
                                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", item.mislip_no));
                                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("sapMesg", sapErrorMessage));
                                        linkconn.ExecuteNow($"INSERT INTO POST_ERROR VALUES(miSlipNo, SYSDATE, sapMesg)");

                                        // Update data in TPHEADER (existing behavior).
                                        linkconn.parameters.Clear();
                                        linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", item.mislip_no));
                                        linkconn.ExecuteNow($"UPDATE TPHEADER SET POST_DATE = SYSDATE WHERE MISLIP_NO = miSlipNo");
                                    }
                                    finally
                                    {
                                        // Both API/RFC failures reach here, even if the legacy error write fails.
                                        RecordCostRollupFailure(item.mislip_no, sapErrorMessage);
                                    }
                                }
                            }
                        }
                    }
                    catch (CostRollup.CostRollupException)
                    {
                        // Do not continue sending slips when the enabled guard is unavailable.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"{func} Loop Error [{item.mislip_no}]: {ex.Message}");
                    }
                }
            }
            catch (CostRollup.CostRollupException ex)
            {
                Environment.ExitCode = 1;
                log.Error($"{func}: Cost Rollup stopped this posting run.", ex);
            }
            catch (Exception ex)
            {
                log.Error($"{func} Critical Error: {ex.Message}");
            }
            finally
            {
                foreach (string miSlipNo in processedMiSlips)
                {
                    linkconn.parameters.Clear();
                    linkconn.parameters.Add(new KeyValuePair<string, dynamic>("curStat", Convert.ToInt32(curStat)));
                    linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", miSlipNo));
                    linkconn.ExecuteNow($"UPDATE TPHEADER SET STATUS = curStat WHERE MISLIP_NO = miSlipNo");
                }
            }

            //End stop watch
            stopWatch.Stop();
            WriteLogStopwatchInfo(stopWatch, func);
        }

        public async Task TranferToSmartAsync(string user_name, string miSlipNo)
        {
            const string func = "TranferToSmartAsync";

            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();
            try
            {
                var linkconn = new MyRepository.Invoke(ConnectionString);
                string sql = @" SELECT CTN FROM PULLLIST_FIFOLOG where MI_SLIPNUMBER = miSlipNo ";
                linkconn.parameters.Clear();
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", miSlipNo));
                var data = linkconn.Get<PULLLIST_FIFOLOG>(sql);

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                foreach (var item in data)
                {
                    var query = HttpUtility.ParseQueryString(string.Empty);
                    query["ctn"] = item.CTN ?? "";
                    query["pl_no"] = miSlipNo ?? "";
                    query["user"] = user_name ?? "";

                    string fullUrl = $"{ConfirmCtn}?{query}";
                    string statusMsg;

                    try
                    {
                        using (var content = new StringContent("", Encoding.UTF8, "text/plain"))
                        using (var response = await _httpClient.PostAsync(fullUrl, content))
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                statusMsg = responseBody;
                                log.Info($"{func}: MiSlipNo {miSlipNo} | CTN {item.CTN} Success => {responseBody}");
                            }
                            else
                            {
                                statusMsg = $"Error {(int)response.StatusCode}: {response.ReasonPhrase}";
                                log.Error($"{func}: MiSlipNo {miSlipNo} | CTN {item.CTN} Fail => {statusMsg} | Body: {responseBody}");
                            }
                        }
                    }
                    catch (TaskCanceledException) // Handles Timeout
                    {
                        statusMsg = "Timeout";
                        log.Error($"{func}: Timeout for CTN {item.CTN}");
                    }
                    catch (Exception ex)
                    {
                        statusMsg = $"Exception: {ex.Message}";
                        log.Error($"{func}: Exception for CTN {item.CTN} => {ex.Message}");
                    }

                    UpdateStatus(statusMsg, item.CTN, miSlipNo);
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("{0}: MiSlipNo. {1} => {2}", func, miSlipNo, ex.Message));
            }

            //End stop watch
            stopWatch.Stop();
            WriteLogStopwatchInfo(stopWatch, string.Format("{0}: MiSlipNo. {1}", func, miSlipNo));
        }

        public async Task<TranferToSAPModel> TranferToSAPAsync(string miSlipNo, string postDate, string docDate, List<data_header> headerDetails)
        {
            const string func = "TranferToSAPAsync";
            TranferToSAPModel result = new TranferToSAPModel();

            //start stop watch
            Stopwatch stopWatch = Stopwatch.StartNew();
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, SAPUrl))
                {
                    #region Headers
                    request.Headers.Add("Header", SapUsername);
                    request.Headers.Add("X-Client-Secret", ClientSecretKey);
                    request.Headers.Add("X-Client-Id", ClientId);

                    string rawCredentials = $"{SapUsername}:{SapPassword}";
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(rawCredentials);
                    string base64Credentials = Convert.ToBase64String(bytes);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);
                    #endregion

                    #region JSON Body
                    GoodsMovementRequestModel model = new GoodsMovementRequestModel();

                    // Header
                    model.Data.Header.PrUname = SapUsername;
                    model.Data.Header.PstngDate = DateTime.ParseExact(postDate, "yyyyMMdd", CultureInfo.InvariantCulture);
                    model.Data.Header.RefDocNoLong = miSlipNo;
                    model.Data.Header.DocDate = DateTime.ParseExact(docDate, "yyyyMMdd", CultureInfo.InvariantCulture);

                    // Item
                    foreach (var item in headerDetails)
                    {
                        GoodsMovementItemModel goodsMovementItem = new GoodsMovementItemModel
                        {
                            Material = item.material?.ToUpper(),
                            Plant = item.plant?.ToUpper(),
                            StgeLoc = item.fsloc?.ToUpper(),
                            MoveStloc = item.tsloc?.ToUpper(),
                            Batch = item.batch?.ToUpper(),
                            MoveType = item.mvt_type?.ToUpper(),
                            EntryQnt = item.batch_issue,
                            MoveBatch = item.mvbatch?.ToUpper()
                        };

                        if (EnableToSendEntryUOMToSAP && !string.IsNullOrEmpty(item.uom))
                        {
                            string sapUom = string.Empty;
                            string cleanUom = item.uom.Replace("\"", "").Replace("'", "").Trim().ToUpper();
                            if (string.IsNullOrEmpty(cleanUom) && (item.uom.Contains("\"") || item.uom.Contains("'")))
                            {
                                sapUom = "IN "; // Ensure "IN " is the correct code in your SAP T006 table
                            }
                            else
                            {
                                sapUom = cleanUom;
                            }
                            goodsMovementItem.EntryUom = sapUom;
                        }
                        else
                        {
                            // Because of the JsonIgnore attribute, setting this to null means
                            // System.Text.Json will automatically cut it from the final JSON!
                            goodsMovementItem.EntryUom = null;
                        }

                        model.Data.Item.Add(goodsMovementItem);
                    }
                    #endregion

                    try
                    {
                        StringContent content = new StringContent(JsonSerializer.Serialize(model), Encoding.UTF8, "application/json");
                        request.Content = content;

                        using (HttpResponseMessage response = await _httpClient.SendAsync(request))
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                //log.Info($"{func}: MiSlipNo {miSlipNo} Success => {responseBody}");
                                try
                                {
                                    GoodsMovementResponseModel responseModel = JsonSerializer.Deserialize<GoodsMovementResponseModel>(responseBody);
                                    if (responseModel.Data.Result.Results != null && responseModel.Data.Result.Results.Count > 0)
                                    {
                                        GoodsMovementResultItem resultItem = responseModel.Data.Result.Results.FirstOrDefault();
                                        log.Info($"{func}: MiSlipNo {miSlipNo} Success! AP returned MatDoc: {resultItem.MatDoc}");
                                        log.Info($"{func}: MiSlipNo {miSlipNo} Message: {resultItem.Message}");

                                        //****************
                                        // * S = Success *
                                        // * W = Warning *
                                        // * E = Error   *
                                        //****************
                                        if (resultItem.PostStatus.Equals("S") || resultItem.PostStatus.Equals("W"))
                                            result.IsSuccess = true;

                                        result.MatDoc = resultItem.MatDoc?.Trim();
                                        result.MoveType = resultItem.MoveType;
                                        result.Batch = resultItem.Batch;
                                        result.PostStatus = resultItem.PostStatus;
                                        result.Message = resultItem.Message.Trim();
                                    }
                                    else
                                    {
                                        log.Info($"{func}: MiSlipNo {miSlipNo} Success => The API returned success, but the Result array was empty.");
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    log.Error($"{func}: Could not deserialize error response into JSON. => {ex.Message}");
                                }
                            }
                            else
                            {
                                GoodsMovementErrorModel errorModel = JsonSerializer.Deserialize<GoodsMovementErrorModel>(responseBody);
                                log.Error($"{func}: MiSlipNo {miSlipNo} Fail => Body: {responseBody}");

                                result.Message = errorModel.Desc.Trim();
                            }
                        }
                    }
                    catch (TaskCanceledException) // Handles Timeout
                    {
                        log.Error($"{func}: Timeout for MiSlipNo {miSlipNo}");
                    }
                    catch (Exception ex)
                    {
                        log.Error($"{func}: Exception for MiSlipNo {miSlipNo} => {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(string.Format("{0}: MiSlipNo. {1} => {2}", func, miSlipNo, ex.Message));
            }

            //End stop watch
            stopWatch.Stop();
            WriteLogStopwatchInfo(stopWatch, string.Format("{0}: MiSlipNo. {1}", func, miSlipNo));

            return result;
        }

        public void UpdateStatus(string msg, string ctn, string miSlipNo)
        {
            const string func = "UpdateStatus";
            try
            {
                var linkconn = new MyRepository.Invoke(ConnectionString);
                linkconn.parameters.Clear();
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("msg", msg));
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("ctn", ctn));
                linkconn.parameters.Add(new KeyValuePair<string, dynamic>("miSlipNo", miSlipNo));
                linkconn.ExecuteNow($" UPDATE PULLLIST_FIFOLOG SET STATUS_TRANFER = msg where CTN = ctn AND MI_SLIPNUMBER = miSlipNo");
            }
            catch (Exception ex)
            {
                log.Error(func + " : " + ex.Message);
            }
        }

        private void WriteLogStopwatchInfo(Stopwatch stopWatch, string title)
        {
            // Get the elapsed time as a TimeSpan value.
            TimeSpan ts = stopWatch.Elapsed;
            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            log.Info(string.Format("{0} : {1}", title, elapsedTime));
        }

        private void WriteLogStopwatchError(Stopwatch stopWatch, string title)
        {
            // Get the elapsed time as a TimeSpan value.
            TimeSpan ts = stopWatch.Elapsed;
            // Format and display the TimeSpan value.
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
            log.Error(string.Format("{0} : {1}", title, elapsedTime));
        }

        // Helper Method
        private string BuildSql(string mvtType, string prdDate, string expDate, string batName, string miSlip)
        {
            if (mvtType == "315")
            {
                string sql = "  SELECT '{0}' MVT_TYPE, '{1}' PRD_DATE, '{2}' EXP_DATE, a.DOC_DATE, " +
                    "                 TO_CHAR(SYSDATE, 'yyyyMMdd') AS XDOC_DATE, a.PLANT, {3} FSLOC, {4} TSLOC, " +
                    "                 CASE WHEN SUBSTR(a.FROM_SLOC, 0, 1) = 'S' AND SUBSTR(a.TO_SLOC, 0, 1) = 'S' THEN c.BATCH ELSE '{5}' END BATCH," +
                    "                 CASE WHEN SUBSTR(a.FROM_SLOC, 0, 1) = 'S' AND SUBSTR(a.TO_SLOC, 0, 1) = 'S' THEN c.BATCH ELSE '{6}' END MVBATCH," +
                    "                 a.ISSUE_BY, a.ISSUE_DATE, a.RECEIVE_BY, a.RECEIVE_DATE, " +
                    "                 c.BATCH_ISSUE, c.MATERIAL " +
                    "          FROM TPHEADER a " +
                    "          LEFT JOIN ( " +
                    "                     SELECT MATERIAL, MISLIP_NO, SUM(BATCH_ISSUE) BATCH_ISSUE, BATCH " +
                    "                     FROM TPDETAIL " +
                    "                     WHERE BATCH_ISSUE > 0 " +
                    "                     GROUP BY MATERIAL, MISLIP_NO, BATCH " +
                    "                 ) c ON c.MISLIP_NO = a.MISLIP_NO " +
                    "          WHERE a.MISLIP_NO = '{7}'";
                return string.Format(sql, mvtType, prdDate, expDate, FSLocField, TSLocField, batName, batName, miSlip);
            }
            else
            {
                string sql = "  SELECT '{0}' MVT_TYPE, '{1}' PRD_DATE, '{2}' EXP_DATE, TO_CHAR(SYSDATE, 'yyyyMMdd') AS XDOC_DATE, {3} FSLOC, {4} TSLOC, " +
                    "                 a.DOC_DATE, a.PLANT, a.ISSUE_BY, a.ISSUE_DATE, a.RECEIVE_BY, a.RECEIVE_DATE, " +
                    "                 b.MATERIAL, b.UOM, " +
                    "                 c.BATCH, c.BATCH_ISSUE, " +
                    "                 CASE WHEN SUBSTR(a.FROM_SLOC, 0, 1) = 'S' AND SUBSTR(a.TO_SLOC, 0, 1) = 'S' THEN c.BATCH ELSE '{5}' END MVBATCH " +
                    "          FROM TPHEADER a " +
                    "          LEFT JOIN TPITEMS b ON a.MISLIP_NO = b.MISLIP_NO " +
                    "          LEFT JOIN TPDETAIL c ON a.MISLIP_NO = c.MISLIP_NO and b.MATERIAL = c.MATERIAL " +
                    "          WHERE a.MISLIP_NO = '{6}' AND c.BATCH_ISSUE > 0 ";
                return string.Format(sql, mvtType, prdDate, expDate, FSLocField, TSLocField, batName, miSlip);
            }
        }
    }
}
