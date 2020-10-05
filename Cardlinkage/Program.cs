using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using log4net;
using System.Data;
using Sterling.MSSQL;
using System.Security.Cryptography;
using System.IO;
using Newtonsoft.Json;
using RestSharp;
using System.Configuration;

namespace Cardlinkage
{
    class Program
    {
        static string FioranoBaseUrl = ConfigurationManager.AppSettings["FioranoBaseUrl"].ToString();
        private static readonly ILog logger =
              LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        static void Main(string[] args)
        {
            string appname = Process.GetCurrentProcess().ProcessName + ".exe";
            if (CheckIfAppIsRunning(appname))
            {
                Console.WriteLine(string.Format("{0} is already running...\r\nExiting", appname));
            }
            else
            {
                logger.Info(string.Format("Starting {0}...", appname));
                Console.WriteLine(string.Format("Starting {0}...", appname));
                while (true)
                {
                    try
                    {
                        StartProcessor();
                        Thread.Sleep(60000); //wait for 60 seconds before rerunning the process
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            }
        }
        public async static void StartProcessor()
        {
            Console.WriteLine("Retrieving records....");
            var recs = GetRecords();

            bool hasRecords = recs.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
            if (hasRecords)
            {
                int cnt = recs.Tables[0].Rows.Count;
                if (cnt > 0)
                {
                    Console.WriteLine(cnt + " records retrieved!!!");
                    for (int i = 0; i < cnt; i++)
                    {
                        var remarks = string.Empty;
                        DataRow dr = recs.Tables[0].Rows[i];
                        var id = dr["ID"].ToString().Trim();
                        //var pan = dr["PAN"].ToString().Trim();
                        var encpan = dr["PAN"].ToString().Trim();
                        var pan = IBS_Decrypt(encpan);
                        var stat = "88";
                        var del = dr["CARDDELIVERY"].ToString().Trim();

                        int proc = UpdateRequestTable(stat, id, encpan, del);
                        //int proc = UpdateRequestTable(stat, id, pan, del);
                        var chk = "0";
                        if (proc == 1)
                        {
                            Console.WriteLine("Processing record " + id + " for pan " + pan.Substring(0, 6) + "*".PadLeft(pan.Length - 10, '*') + pan.Substring(pan.Length - 4, 4));
                            var cust_id = dr["CUSTOMERNUMBER"].ToString().Trim();
                            //account number
                            var account = dr["ACCOUNT"].ToString().Trim();
                           

                            try
                            {
                                var accountInfo = await FioranoGetAccountFullInfo(account);
                                var BVN = string.Empty;
                                if (accountInfo.BankAccountFullInfo != null && !string.IsNullOrEmpty(accountInfo.BankAccountFullInfo.CUS_NUM))
                                {
                                    BVN = accountInfo.BankAccountFullInfo.BVN;
                                }

                                if (account.Substring(0, 2) == "05")
                                {
                                    chk = "1";
                                }
                                var accountType = dr["ACCOUNTTYPE"].ToString().Trim();
                                var currencyCode = GetCurrency(dr["CURRENCYCODE"].ToString().Trim());
                                var address = BreakAddress(dr["RESADDRESS"].ToString().Trim());
                                var addy1 = address[0]; var addy2 = address[1];
                                var title = dr["TITLE"].ToString().Trim();
                                var firstName = Trunc(dr["CARDFIRSTNAME"].ToString().Trim(), 20);
                                var middleName = Trunc(dr["CARDMIDNAME"].ToString().Trim(), 10);
                                var surName = Trunc(dr["CARDSURNAME"].ToString().Trim(), 20);
                                var nameOnCard = Trunc(dr["CUS_NAME"].ToString().Trim(), 26);
                                var branch = dr["BRANCHCODE"].ToString().Trim();
                                var dob = dr["BIRTHDAY"].ToString().Trim();
                                var gender = dr["SEX"].ToString().Trim();
                                var issuer = dr["ISSUERNUMBER"].ToString().Trim();
                                var user = GetUsername(dr["ADDEDBY"].ToString().Trim());
                                var seq = (dr["CARDSEQUENCE"].ToString().Trim()).PadLeft(3, '0');
                                var city = dr["CUSCITY"].ToString().Trim();
                                var region = dr["CUSREGION"].ToString().Trim();
                                var country = "NGN";
                                var pindel = dr["PINDELIVERY"].ToString().Trim();

                                //if (VerifyRecordExist(issuer, pan, seq, del, pindel))
                                if (VerifyRecordExist(issuer, pan, seq, del, pindel))
                                {
                                    var cus_id = InsertCustomer(issuer, cust_id, title, firstName, middleName, surName, nameOnCard, addy1, addy2, city, region, country, user, chk);
                                    if (cust_id != "")
                                    {
                                        int insAcc = InsertAccount(issuer, account, accountType, currencyCode, user);
                                        if (insAcc > 0)
                                        {
                                            int insCusAcc = InsertCustomerAccount(issuer, cus_id, account, accountType, user);
                                            if (insCusAcc > 0)
                                            {
                                                int updCardDetails = UpdatePostilionCard(issuer, pan, seq, del, pindel, cus_id, branch, user, accountType, BVN);
                                                if (updCardDetails > 0)
                                                {
                                                    int insCardAcc = InsertCardAccounts(issuer, pan, seq, account, accountType, user);
                                                    if (insCardAcc > 0)
                                                    {
                                                        remarks = string.Empty;
                                                        int deleteVir = DeleteVirtualCustomer(issuer, pindel);
                                                        stat = "11";
                                                        var cardProg = GetCardProgram(id);
                                                        int blk = BlockExistingCardVariants(cust_id, issuer, chk, pan, cardProg);
                                                    }
                                                    else
                                                    {
                                                        remarks = "UNABLE TO UPDATE PC_CARD_ACCOUNTS_" + issuer + "_A TABLE";
                                                        stat = "77";
                                                    }
                                                }
                                                else
                                                {
                                                    remarks = "UNABLE TO UPDATE PC_CARDS_" + issuer + "_A TABLE";
                                                    stat = "77";
                                                }
                                            }
                                            else
                                            {
                                                remarks = "UNABLE TO UPDATE PC_CUSTOMER_ACCOUNTS_" + issuer + "_A TABLE";
                                                stat = "77";
                                            }
                                        }
                                        else
                                        {
                                            remarks = "UNABLE TO UPDATE PC_ACCOUNTS_" + issuer + "_A TABLE";
                                            stat = "77";
                                        }
                                    }
                                    else
                                    {
                                        remarks = "UNABLE TO UPDATE PC_CUSTOMERS_" + issuer + "_A TABLE";
                                        stat = "77";
                                    }

                                    if ((remarks != "") && (remarks != null) && (remarks != "null"))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        logger.Error("Please retry " + id);
                                        logger.Error("Could not process record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " with Error - " + remarks + "!!!");
                                        Console.WriteLine("Could not process record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " with Error - " + remarks + "!!!");
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        logger.Error("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " Processed Successfully On Postilion!!!");
                                        Console.WriteLine("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " Processed Successfully On Postilion!!!");
                                        Console.ResetColor();
                                    }

                                    //int comp = UpdateRequestTable2(stat, id, pan, del, remarks);
                                    int comp = UpdateRequestTable2(stat, id, encpan, del, remarks);
                                    if (comp == 1)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        logger.Error("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " Updated Successfully on card Request!!!");
                                        Console.WriteLine("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " Updated Successfully on card Request!!!");
                                        Console.ResetColor();
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        logger.Error("Please retry " + id);
                                        logger.Error("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                                        Console.WriteLine("Record " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                                        Console.ResetColor();
                                    }
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    logger.Error("Please retry " + id);
                                    logger.Error("UNABLE TO VERIFY RECORD " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                                    Console.WriteLine("UNABLE TO VERIFY RECORD " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                                    Console.ResetColor();
                                    remarks = "UNABLE TO VERIFY RECORD";
                                    stat = "77";
                                    //int comp = UpdateRequestTable2(stat, id, pan, del, remarks);
                                    int comp = UpdateRequestTable2(stat, id, encpan, del, remarks);
                                }
                            }
                            catch(Exception ex)
                            {
                                logger.Error("Unabel to get account full info. Message:" + ex.Message + ".stack:" + ex.StackTrace);
                            }
                          
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            logger.Error("Please retry " + id);
                            logger.Error("UNABLE TO UPDATE CARDREQUEST FOR  RECORD " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                            Console.WriteLine("UNABLE TO UPDATE CARDREQUEST  RECORD " + id + " for pan " + "*".PadLeft(pan.Length - 4, '*') + pan.Substring(pan.Length - 4, 4) + " not Updated Successfully on card Request!!!");
                            Console.ResetColor();
                        }
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    logger.Error("NO RECORDS RETRIEVED FOR PROCESSING!!!");
                    Console.WriteLine("NO RECORDS RETRIEVED FOR PROCESSING!!!");
                    Console.ResetColor();
                }
            }
        }
        private static string Trunc(string val, int len)
        {
            var nVal = string.Empty;
            val = val.Replace("  ", " ").Replace("  ", " ").Replace("<", "").Replace(">", "");
            if (val != "")
            {
                if (val.Length > len)
                {
                    nVal = val.Substring(0, len);
                }
                else
                {
                    nVal = val;
                }
            }
            else
            {
                nVal = val;
            }
            return nVal;
        }
        private static string[] BreakAddress(string val)
        {
            var nVal = new string[2];

            if (val.Length > 30)
            {
                nVal[0] = Trunc(val, 30);
                nVal[1] = Trunc(val.Substring(30,val.Length - 30), 30);
            }
            else
            {
                nVal[0] = val;
                nVal[1] = string.Empty;
            }
            return nVal;
        }
        private static string Name(string names)
        {
            var nm = names;

            if (names.Length > 20)
            {
                nm = names.Substring(0, 20);
            }

            return nm;
        }
        public static DataSet GetRecords()
        {
            var recs = new DataSet();
            string sql = "SELECT * FROM CardRequest WHERE STATUSFLAG = '10' ";
            //string sql = "SELECT * FROM CardRequest WHERE ID = '1189368' ";
            Connect cn = new Connect("CardApp");
            cn.Persist = true;
            cn.SetSQL(sql);
            recs = cn.Select();
            cn.CloseAll();            

            return recs;
        }
        public static string GetUsername(string userid)
        {
            var user = "cardRequest";
            string sql = "SELECT UserName FROM [Users] WHERE  ID = @id ";
            Connect cn = new Connect("CardApp");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@id", userid);
            DataSet ds = cn.Select();
            cn.CloseAll();

            bool hasRecords = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
            if (hasRecords)
            {
                int cnt = ds.Tables[0].Rows.Count;
                if (cnt == 1)
                {
                    DataRow dr = ds.Tables[0].Rows[0];
                    user = dr["UserName"].ToString().Trim();
                }
            }

            return user;
        }
        public static int UpdateRequestTable(string stat,string id,string pan,string del)
        {
            int upd = 0;
            string sql = "UPDATE CardRequest SET STATUSFLAG = @status WHERE ID = @id AND PAN = @pan AND CARDDELIVERY = @delivery";
            //string sql = "UPDATE CardRequest SET STATUSFLAG = @status WHERE ID = @id AND PAN = @pan AND BRANCHCODE = @delivery";
            Connect cn = new Connect("CardApp");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@status", stat);
            cn.AddParam("@id", id);
            cn.AddParam("@pan", pan);
            cn.AddParam("@delivery", del);
            upd = cn.Update();
            cn.CloseAll();

            return upd;
        }
        public static int UpdateRequestTable2(string stat, string id, string pan, string del,string rem)
        {
            int upd = 0;
            if ((rem != "") && (rem != null) && (rem != "null"))
            {
                string sql = "UPDATE CardRequest SET STATUSFLAG = @status, DATEAUTHORISED = @dt, REMARK = @rem WHERE ID = @id AND PAN = @pan AND CARDDELIVERY = @delivery";
                Connect cn = new Connect("CardApp");
                cn.Persist = true;
                cn.SetSQL(sql);
                cn.AddParam("@status", stat);
                cn.AddParam("@id", id);
                cn.AddParam("@pan", pan);
                cn.AddParam("@delivery", del);
                cn.AddParam("@dt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                cn.AddParam("@rem", rem);
                upd = cn.Update();
                cn.CloseAll();
            }
            else
            {
                string sql = "UPDATE CardRequest SET STATUSFLAG = @status, DATEAUTHORISED = @dt, REMARK = NULL WHERE ID = @id AND PAN = @pan AND CARDDELIVERY = @delivery ";
                Connect cn = new Connect("CardApp");
                cn.Persist = true;
                cn.SetSQL(sql);
                cn.AddParam("@status", stat);
                cn.AddParam("@id", id);
                cn.AddParam("@pan", pan);
                cn.AddParam("@delivery", del);
                cn.AddParam("@dt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                upd = cn.Update();
                cn.CloseAll();
            }
           

            return upd;
        }
        public static int BlockExistingCardVariants(string customer_id,string issuer,string chk,string pan,string cardprogram)
        {
            var resp = 0;

            var cus_Id = customer_id;

            string sql = string.Empty;

            if (chk == "1")
            {
                cus_Id = customer_id;
                if (pan.Substring(0, 3) == "506")
                {
                    sql = "UPDATE pc_cards_" + issuer + "_A SET hold_rsp_code = '88' WHERE customer_id IN (@customer_id,@cus_Id) AND card_program LIKE '%IMAL_VERVE%' AND PAN != @pan";
                }
                else if (pan.Substring(0, 1) == "5")
                {
                    if ((cardprogram != "SBP_MASTER_UTIL") && (cardprogram != "SBP_MASTER_SPECTA"))
                    {
                        sql = "UPDATE pc_cards_" + issuer + "_A SET hold_rsp_code = '88' WHERE customer_id IN (@customer_id,@cus_Id) AND card_program LIKE '%IMAL_MASTER%' AND PAN != @pan AND card_program NOT IN ('SBP_MASTER_UTIL','SBP_MASTER_SPECTA')";
                    }
                }
            }
            else if (customer_id.Length < 7)
            {
                cus_Id = customer_id.PadLeft(7, '0');
                if (pan.Substring(0, 3) == "506")
                {
                    sql = "UPDATE pc_cards_" + issuer + "_A SET hold_rsp_code = '88' WHERE customer_id IN (@customer_id,@cus_Id) AND card_program LIKE '%VERVE%' AND PAN != @pan AND card_program NOT IN ('IMAL_VERVECPA','SBP_IMAL_MASTER')";
                }
                else if (pan.Substring(0, 1) == "5")
                {
                    if ((cardprogram != "SBP_MASTER_UTIL") && (cardprogram != "SBP_MASTER_SPECTA"))
                    {
                        sql = "UPDATE pc_cards_" + issuer + "_A SET hold_rsp_code = '88' WHERE customer_id IN (@customer_id,@cus_Id) AND card_program LIKE '%MASTER%' AND PAN != @pan AND card_program NOT IN ('SBP_MASTER_UTIL','SBP_MASTER_SPECTA')";
                    }
                }
                else if (pan.Substring(0, 1) == "4")
                {
                    sql = "UPDATE pc_cards_" + issuer + "_A SET hold_rsp_code = '88' WHERE customer_id IN (@customer_id,@cus_Id) AND card_program LIKE '%VISA%' AND PAN != @pan";
                }
            }


            if (sql != "")
            {
                Connect cn = new Connect("Postcard");
                cn.SetSQL(sql);
                cn.AddParam("@customer_id", customer_id);
                cn.AddParam("@cus_Id", cus_Id);
                cn.AddParam("@pan", pan);
                resp = cn.Update();
                cn.CloseAll();
            }

            if (resp > 0)
            {
                logger.Error(resp + " old cards blocked!!!");
            }

            return resp;
        }
        public static bool VerifyRecordExist(string issuer,string pan,string seq, string branch, string pindel)
        {
            bool exists = false;
            var oldbranch = GetOldBranch(branch);

            string sql = "SELECT * FROM pc_cards_"+issuer+ "_A WHERE pan = @pan AND seq_nr = @seq AND branch_code IN (@branch_code,@oldbranch) AND customer_id = @pindelivery";
            Connect cn = new Connect("PostCard");
            cn.Persist = true;
            cn.SetSQL(sql);            
            cn.AddParam("@pan", pan);
            cn.AddParam("@seq", seq);
            cn.AddParam("@branch_code", branch);
            cn.AddParam("@oldbranch", oldbranch);
            cn.AddParam("@pindelivery", pindel);
            DataSet ds = cn.Select();
            cn.CloseAll();

            bool hasRecords = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
            if (hasRecords)
            {
                int cnt = ds.Tables[0].Rows.Count;
                if (cnt >= 1)
                {
                    exists = true;
                }
            }

            return exists;
        }
        //Delete virtual customer from pc_customer 
        public static int DeleteVirtualCustomer(string issuer, string customer_id)
        {
            var upd = 0;
            string sql = "DELETE pc_customers_"+issuer+"_A WHERE customer_id = @customer";
            Connect cn = new Connect("PostCard");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@customer", customer_id);
            upd = cn.Update();
            cn.CloseAll();

            return upd;
        }
        //Update Card Details
        public static int UpdatePostilionCard(string issuer, string pan, string seq, string carddelivery, string pindelivery,string customer_id,string branch,string user,string accountType,string BVN)
        {
            int upd = 0;

            var oldbranch = GetOldBranch(carddelivery);

            string sql = "UPDATE pc_cards_"+issuer+ "_A SET customer_id = @customer_id,branch_code = @branch,last_updated_date = @dt,default_account_type = @acctype,last_updated_user = @user,date_issued = @dt, issuer_reference=@BVN" +
                " WHERE pan = @pan AND seq_nr = @seq AND branch_code IN (@carddelivery,@oldbranch) AND customer_id = @pindelivery";
            Connect cn = new Connect("PostCard");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@pan", pan);
            cn.AddParam("@seq", seq);
            cn.AddParam("@carddelivery", carddelivery);
            cn.AddParam("@oldbranch", oldbranch);
            cn.AddParam("@pindelivery", pindelivery);
            cn.AddParam("@acctype", accountType);
            cn.AddParam("@branch", branch);
            cn.AddParam("@customer_id", customer_id);
            cn.AddParam("@dt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
            cn.AddParam("@user", user);
            cn.AddParam("@BVN", BVN);
            upd = cn.Update();
            cn.CloseAll();

            return upd;
        }
        //public static int UpdatePostilionCard(string issuer, string pan, string seq, string carddelivery, string pindelivery, string customer_id, string branch, string user, string accountType)
        //{
        //    int upd = 0;

        //    var oldBranch = GetOldBranch(carddelivery);

        //    string sql = "UPDATE pc_cards_" + issuer + "_A SET customer_id = @customer_id,branch_code = @branch,last_updated_date = @dt,default_account_type = @acctype,last_updated_user = @user,date_issued = @dt" +
        //        " WHERE pan = @pan AND seq_nr = @seq AND branch_code = @carddelivery AND customer_id = @pindelivery";
        //    Connect cn = new Connect("PostCard");
        //    cn.Persist = true;
        //    cn.SetSQL(sql);
        //    cn.AddParam("@pan", pan);
        //    cn.AddParam("@seq", seq);
        //    cn.AddParam("@carddelivery", carddelivery);
        //    cn.AddParam("@carddelivery", carddelivery);
        //    cn.AddParam("@pindelivery", pindelivery);
        //    cn.AddParam("@acctype", accountType);
        //    cn.AddParam("@branch", branch);
        //    cn.AddParam("@customer_id", customer_id);
        //    cn.AddParam("@dt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
        //    cn.AddParam("@user", user);
        //    upd = cn.Update();
        //    cn.CloseAll();

        //    return upd;
        //}
        //Get Old T24 Branch
        public static string GetOldBranch(string t24branch)
        {
            var oldbranch = t24branch.Trim();
            var t24Br = t24branch.Trim();
            var len = (t24branch.Trim()).Length;
            var pd = string.Empty;

            if ( len > 9)
            {
                pd = (t24branch.Trim()).Substring(9,len-9);
                t24Br = t24branch.Trim().Substring(0,9);
            }

            string sql = "SELECT BANKSBRACODE FROM BranchInfo WHERE T24BRANCHID = @rec_Id";
            Connect cn = new Connect("CardApp");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@rec_Id", t24Br);
            DataSet ds = cn.Select();
            cn.CloseAll();

            bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
            if (hasTables)
            {
                int rws = ds.Tables[0].Rows.Count;
                if (rws > 0)
                {
                    DataRow dr = ds.Tables[0].Rows[0];
                    if(dr["BANKSBRACODE"].ToString() != "")
                    {
                        oldbranch = dr["BANKSBRACODE"].ToString();
                    }
                }
            }
            return oldbranch + pd;
        }
        //Get Card_program
        public static string GetCardProgram(string recId)
        {
            var cardProg = string.Empty;

            string sql = "SELECT p.Description FROM CardRequest r INNER JOIN Product p ON r.PRODUCT = p.ID  WHERE r.ID = @rec_Id";
            Connect cn = new Connect("CardApp");
            cn.Persist = true;
            cn.SetSQL(sql);
            cn.AddParam("@rec_Id", recId);
            DataSet ds = cn.Select();
            cn.CloseAll();

            bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
            if (hasTables)
            {
                int rws = ds.Tables[0].Rows.Count;

                if (rws > 0)
                {
                    DataRow dr = ds.Tables[0].Rows[0];
                    var desc = dr[0].ToString();

                    if (desc != "")
                    {
                        var sp = desc.Split('|');
                        if (sp.Length > 0)
                        {
                            cardProg = sp[0];
                        }
                    }
                }
            }

            return cardProg;
        }
        //Get Currency
        public static string GetCurrency(string currency)
        {
            var curr = "566";

            if (currency.Length == 3)
            {
                curr = currency;
            }
            else
            {
                switch (currency)
                {
                    case "1":
                        curr = "566";
                        break;
                    case "2":
                        curr = "840";
                        break;
                }
            }
            return curr;
        }
        //Insert ustomer Details into Postillion
        public static string InsertCustomer(string issuer, string customer_id, string title, string fname, string initials, string lastname, string nameOnCard, string addy1, string addy2, string city, string region, string country, string user, string chk)
        {
            var rws = 0; var data = string.Empty;

            var cus_Id = customer_id;

            if (chk == "1")
            {
                cus_Id = customer_id;
            }
            else if (customer_id.Length < 7)
            {
                cus_Id = customer_id.PadLeft(7, '0');
            }

            try
            {
                string sql = "SELECT customer_id FROM pc_customers_" + issuer + "_A WHERE customer_id IN (@customer_id,@cus_Id) ORDER BY customer_id DESC";
                Connect cn = new Connect("PostCard")
                {
                    Persist = true
                };
                cn.SetSQL(sql);
                cn.AddParam("@customer_id", customer_id);
                cn.AddParam("@cus_Id", cus_Id);
                DataSet ds = cn.Select();
                cn.CloseAll();

                bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
                if (hasTables)
                {
                    rws = ds.Tables[0].Rows.Count;

                    if (rws <= 0)
                    {
                        string isql = @"INSERT INTO pc_customers_" + issuer + "_A (issuer_nr, customer_id, c1_title,c1_first_name, c1_initials,c1_last_name, c1_name_on_card, postal_address_1,postal_address_2, postal_city, postal_region, postal_country, vip, last_updated_date, last_updated_user) " +
                                    "VALUES (@issuer_nr, @customer_id, @title, @fname,@initials, @lastname, @nameOncard, @addy1, @addy2, @city, @region, @country, '0; @dt, @user)";
                        logger.Info("Sql to excecute is ==> " + isql);
                        Connect un = new Connect("PostCard")
                        {
                            Persist = true
                        };
                        un.SetSQL(isql);
                        un.AddParam("@issuer_nr", issuer);
                        //logger.Info("@issuer_nr ==> " + issuer);
                        un.AddParam("@customer_id", cus_Id);
                        //logger.Info("@customer_id ==> " + cus_Id);
                        un.AddParam("@title", title);
                        //logger.Info("@title ==> " + title);
                        un.AddParam("@fname", fname);
                        //logger.Info("@fname ==> " + fname);
                        un.AddParam("@initials", initials);
                        //logger.Info("@initials ==> " + initials);
                        un.AddParam("@lastname", lastname);
                        //logger.Info("@lastname ==> " + lastname);
                        un.AddParam("@nameOncard", nameOnCard);
                        //logger.Info("@nameOnCard ==> " + nameOnCard);
                        un.AddParam("@addy1", addy1);
                        //logger.Info("@addy1 ==> " + addy1);
                        un.AddParam("@addy2", addy2);
                        //logger.Info("@addy2 ==> " + addy2);
                        un.AddParam("@city", city);
                        //logger.Info("@city ==> " + city);
                        un.AddParam("@region", region);
                        //logger.Info("@region ==> " + region);
                        un.AddParam("@country", country);
                        //logger.Info("@country ==> " + country);
                        string updTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff");
                        un.AddParam("@dt", updTime);
                        //logger.Info("@dt ==> " + updTime);
                        un.AddParam("@user", user);
                        //logger.Info("@user ==> " + user);
                        rws = un.Update();
                        un.CloseAll();
                        logger.Error(rws + "rows inserted for " + cus_Id);
                        if (rws > 0)
                        {
                            data = cus_Id;
                        }
                    }
                    else
                    {
                        DataRow dr = ds.Tables[0].Rows[0];
                        data = dr["customer_id"].ToString();
                        logger.Error(data + "already exists for " + cus_Id);
                    }
                }
                else
                {
                    string isql = @"INSERT INTO pc_customers_" + issuer + "_A (issuer_nr, customer_id, c1_title,c1_first_name, c1_initials,c1_last_name, c1_name_on_card, postal_address_1,postal_address_2, postal_city, postal_region, postal_country, vip, last_updated_date, last_updated_user) " +
                                   "VALUES (@issuer_nr, @customer_id, @title, @fname,@initials, @lastname, @nameOncard, @addy1, @addy2, @city, @region, @country, '0', @dt, @user)";
                    logger.Info("Sql to excecute is ==> " + isql);
                    Connect un = new Connect("PostCard")
                    {
                        Persist = true
                    };
                    un.SetSQL(isql);
                    un.AddParam("@issuer_nr", issuer);
                    //logger.Info("@issuer ==> " + issuer);
                    un.AddParam("@customer_id", cus_Id);
                    //logger.Info("@customer_id ==> " + cus_Id);
                    un.AddParam("@title", title);
                    //logger.Info("@title ==> " + title);
                    un.AddParam("@fname", fname);
                    //logger.Info("@fname ==> " + fname);
                    un.AddParam("@initials", initials);
                    //logger.Info("@initials ==> " + initials);
                    un.AddParam("@lastname", lastname);
                    //logger.Info("@lastname ==> " + lastname);
                    un.AddParam("@nameOncard", nameOnCard);
                    //logger.Info("@nameOnCard ==> " + nameOnCard);
                    un.AddParam("@addy1", addy1);
                    //logger.Info("@addy1 ==> " + addy1);
                    un.AddParam("@addy2", addy2);
                    //logger.Info("@addy2 ==> " + addy2);
                    un.AddParam("@city", city);
                    //logger.Info("@city ==> " + city);
                    un.AddParam("@region", region);
                    //logger.Info("@region ==> " + region);
                    un.AddParam("@country", country);
                    //logger.Info("@country ==> " + country);
                    string updTime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff");
                    un.AddParam("@dt", updTime);
                    //logger.Info("@dt ==> " + updTime);
                    un.AddParam("@user", user);
                    //logger.Info("@user ==> " + user);
                    rws = un.Update();
                    logger.Error(rws + "rows inserted for " + cus_Id);
                    un.CloseAll();

                    if (rws > 0)
                    {
                        data = cus_Id;
                    }
                }

            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            return data;
        }
        //Insert Account Details into Postilion
        public static int InsertAccount(string issuer_nr, string account_id, string account_type, string currency_code, string user)
        {
            var rws = 0;

            try
            {
                string sql = @"SELECT * FROM pc_accounts_" + issuer_nr + "_A WHERE account_id = @account_id AND account_type = @acctype";
                Connect cn = new Connect("PostCard");
                cn.Persist = true;
                cn.SetSQL(sql);
                cn.AddParam("@account_id", account_id);
                cn.AddParam("@acctype", account_type);
                DataSet ds = cn.Select();
                cn.CloseAll();

                bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
                if (hasTables)
                {
                    rws = ds.Tables[0].Rows.Count;

                    if (rws <= 0)
                    {
                        string isql = @"INSERT INTO pc_accounts_" + issuer_nr + "_A (issuer_nr,account_id,account_type,currency_code,last_updated_date,last_updated_user) " +
                                "VALUES (@issuer_nr,@account_id,@account_type,@currency_code,@update_date,@update_user)";
                        logger.Info("Sql to excecute is ==> " + isql);
                        Connect un = new Connect("PostCard");
                        un.Persist = true;
                        un.SetSQL(isql);
                        un.AddParam("@issuer_nr", issuer_nr);
                        un.AddParam("@account_id", account_id);
                        un.AddParam("@account_type", account_type);
                        un.AddParam("@currency_code", currency_code);
                        un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                        un.AddParam("@update_user", user);
                        rws = un.Update();
                        un.CloseAll();
                    }
                }
                else
                {
                    string isql = @"INSERT INTO pc_accounts_" + issuer_nr + "_A (issuer_nr,account_id,account_type,currency_code,last_updated_date,last_updated_user) " +
                                "VALUES (@issuer_nr,@account_id,@account_type,@currency_code,@update_date,@update_user)";
                    logger.Info("Sql to excecute is ==> " + isql);
                    Connect un = new Connect("PostCard");
                    un.Persist = true;
                    un.SetSQL(isql);
                    un.AddParam("@issuer_nr", issuer_nr);
                    un.AddParam("@account_id", account_id);
                    un.AddParam("@account_type", account_type);
                    un.AddParam("@currency_code", currency_code);
                    un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                    un.AddParam("@update_user", user);
                    rws = un.Update();
                    un.CloseAll();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return rws;
        }
        //Insert Customer Account Details into Postilion
        public static int InsertCustomerAccount(string issuer_nr, string customer_id, string account_id, string account_type, string user)
        {
            var rws = 0;
            try
            {
                string sql = @"SELECT * FROM pc_customer_accounts_" + issuer_nr + "_A WHERE account_id = @account_id AND customer_id = @customer_id " +
                             " AND account_type = @account_type";
                Connect cn = new Connect("PostCard");
                cn.Persist = true;
                cn.SetSQL(sql);
                cn.AddParam("@account_id", account_id);
                cn.AddParam("@customer_id", customer_id);
                cn.AddParam("@account_type", account_type);
                DataSet ds = cn.Select();
                cn.CloseAll();

                bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
                if (hasTables)
                {
                    rws = ds.Tables[0].Rows.Count;

                    if (rws <= 0)
                    {
                        string isql = @"INSERT INTO pc_customer_accounts_" + issuer_nr + "_A (issuer_nr,customer_id,account_id,account_type,last_updated_date,last_updated_user) " +
                                     "VALUES (@issuer_nr,@customer_id,@account_id,@account_type,@update_date,@update_user)";
                        logger.Info("Sql to excecute is ==> " + isql);
                        Connect un = new Connect("PostCard");
                        un.Persist = true;
                        un.SetSQL(isql);
                        un.AddParam("@issuer_nr", issuer_nr);
                        un.AddParam("@customer_id", customer_id);
                        un.AddParam("@account_id", account_id);
                        un.AddParam("@account_type", account_type);
                        un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                        un.AddParam("@update_user", user);
                        rws = un.Update();
                        un.CloseAll();
                    }
                }
                else
                {
                    string isql = @"INSERT INTO pc_customer_accounts_" + issuer_nr + "_A (issuer_nr,customer_id,account_id,account_type,last_updated_date,last_updated_user) " +
                                     "VALUES (@issuer_nr,@customer_id,@account_id,@account_type,@update_date,@update_user)";
                    logger.Info("Sql to excecute is ==> " + isql);
                    Connect un = new Connect("PostCard");
                    un.Persist = true;
                    un.SetSQL(isql);
                    un.AddParam("@issuer_nr", issuer_nr);
                    un.AddParam("@customer_id", customer_id);
                    un.AddParam("@account_id", account_id);
                    un.AddParam("@account_type", account_type);
                    un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                    un.AddParam("@update_user", user);
                    rws = un.Update();
                    un.CloseAll();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return rws;
        }
        //Insert Card Account Details into Postilion
        public static int InsertCardAccounts(string issuer_nr, string pan, string seq_nr, string account_id, string account_type, string user)
        {
            var rws = 0;
            try
            {
                string sql = @"SELECT * FROM pc_card_accounts_" + issuer_nr + "_A WHERE pan = @pan AND seq_nr = @seq_nr " +
                             "AND account_id = @account_id AND account_type = @account_type";
                Connect cn = new Connect("PostCard");
                cn.Persist = true;
                cn.SetSQL(sql);
                cn.AddParam("@pan", pan);
                cn.AddParam("@seq_nr", seq_nr);
                cn.AddParam("@account_id", account_id);
                cn.AddParam("@account_type", account_type);
                DataSet ds = cn.Select();
                cn.CloseAll();

                bool hasTables = ds.Tables.Cast<DataTable>().Any(table => table.Rows.Count != 0);
                if (hasTables)
                {
                    rws = ds.Tables[0].Rows.Count;

                    if (rws <= 0)
                    {
                        string isql = @"INSERT INTO pc_card_accounts_" + issuer_nr + "_A (issuer_nr,pan,seq_nr,account_id,account_type_nominated," +
                                     "account_type_qualifier,last_updated_date,last_updated_user,account_type) " +
                                     "VALUES (@issuer_nr,@pan,@seq_nr,@account_id,@account_type,'1',@update_date,@update_user,@account_type)";
                        logger.Info("Sql to excecute is ==> " + isql);
                        Connect un = new Connect("PostCard");
                        un.Persist = true;
                        un.SetSQL(isql);
                        un.AddParam("@issuer_nr", issuer_nr);
                        un.AddParam("@pan", pan);
                        un.AddParam("@seq_nr", seq_nr);
                        un.AddParam("@account_id", account_id);
                        un.AddParam("@account_type", account_type);
                        un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                        un.AddParam("@update_user", user);
                        rws = un.Update();
                        un.CloseAll();
                    }
                }
                else
                {
                    string isql = @"INSERT INTO pc_card_accounts_" + issuer_nr + "_A (issuer_nr,pan,seq_nr,account_id,account_type_nominated," +
                                    "account_type_qualifier,last_updated_date,last_updated_user,account_type) " +
                                    "VALUES (@issuer_nr,@pan,@seq_nr,@account_id,@account_type,'1',@update_date,@update_user,@account_type)";
                    logger.Info("Sql to excecute is ==> " + isql);
                    Connect un = new Connect("PostCard");
                    un.Persist = true;
                    un.SetSQL(isql);
                    un.AddParam("@issuer_nr", issuer_nr);
                    un.AddParam("@pan", pan);
                    un.AddParam("@seq_nr", seq_nr);
                    un.AddParam("@account_id", account_id);
                    un.AddParam("@account_type", account_type);
                    un.AddParam("@update_date", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff"));
                    un.AddParam("@update_user", user);
                    rws = un.Update();
                    un.CloseAll();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }

            return rws;
        }
        public static bool CheckIfAppIsRunning(string appname)
        {
            string query = "Select * from Win32_Process Where Name = '" + appname + "'";
            ManagementObjectCollection processList = (new ManagementObjectSearcher(query)).Get();
            return processList.Count > 1;
        }

        public static string IBS_Encrypt(String val)
        {
            var pp = string.Empty;
            MemoryStream ms = new MemoryStream();
            string rsp = "";
            try
            {
                var sharedkeyval = "000000010000001000000011000001010000011100001011000011010001000100010010000100010000110100001011000001110000001100000100000010000000000100000010000000110000010100000111000010110000110100001101";
                sharedkeyval = BinaryToString(sharedkeyval);
                var sharedvectorval = "0000000100000010000000110000010100000111000010110000110100000011";
                sharedvectorval = BinaryToString(sharedvectorval);
                //sharedvectorval = BinaryToString(sharedvectorval);
                byte[] sharedkey = System.Text.Encoding.GetEncoding("utf-8").GetBytes(sharedkeyval);
                byte[] sharedvector = System.Text.Encoding.GetEncoding("utf-8").GetBytes(sharedvectorval);

                TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
                byte[] toEncrypt = Encoding.UTF8.GetBytes(val);

                CryptoStream cs = new CryptoStream(ms, tdes.CreateEncryptor(sharedkey, sharedvector), CryptoStreamMode.Write);
                cs.Write(toEncrypt, 0, toEncrypt.Length);
                cs.FlushFinalBlock();
                pp = Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                Console.WriteLine("Error encrypting pan " + pp.Substring(0, 6) + " * ".PadLeft(pp.Length - 10, '*') + pp.Substring(pp.Length - 4, 4) + " is " + ex.ToString());
                pp = val;
            }
            return pp;
        }

        public static string IBS_Decrypt(string val)
        {
            var pp = string.Empty;

            try
            {
                var sharedkeyval = "000000010000001000000011000001010000011100001011000011010001000100010010000100010000110100001011000001110000001100000100000010000000000100000010000000110000010100000111000010110000110100001101";
                sharedkeyval = BinaryToString(sharedkeyval);
                var sharedvectorval = "0000000100000010000000110000010100000111000010110000110100000011";
                sharedvectorval = BinaryToString(sharedvectorval);
                byte[] sharedkey = Encoding.GetEncoding("utf-8").GetBytes(sharedkeyval);
                byte[] sharedvector = Encoding.GetEncoding("utf-8").GetBytes(sharedvectorval);
                TripleDESCryptoServiceProvider tdes = new TripleDESCryptoServiceProvider();
                byte[] toDecrypt = Convert.FromBase64String(val);
                MemoryStream ms = new MemoryStream();
                CryptoStream cs = new CryptoStream(ms, tdes.CreateDecryptor(sharedkey, sharedvector), CryptoStreamMode.Write);
                cs.Write(toDecrypt, 0, toDecrypt.Length);
                cs.FlushFinalBlock();
                pp = Encoding.UTF8.GetString(ms.ToArray());
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                pp = val;
            }
            return pp;
        }

        private static string BinaryToString(string binary)
        {
            if (string.IsNullOrEmpty(binary))
                throw new ArgumentNullException("binary");

            if ((binary.Length % 8) != 0)
                throw new ArgumentException("Binary string invalid (must divide by 8)", "binary");

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < binary.Length; i += 8)
            {
                string section = binary.Substring(i, 8);
                int ascii = 0;
                try
                {
                    ascii = Convert.ToInt32(section, 2);
                }
                catch
                {
                    throw new ArgumentException("Binary string contains invalid section: " + section, "binary");
                }
                builder.Append((char)ascii);
            }
            return builder.ToString();
        }

        public static async Task<GetAccountFullInfoResponse> FioranoGetAccountFullInfo(string accountNumber)
        {
            var PostParam = JsonConvert.SerializeObject(accountNumber);
            GetAccountFullInfoResponse fioranoFtResponse = new GetAccountFullInfoResponse();
            fioranoFtResponse.BankAccountFullInfo = new BankAccountFullInfoNew();
            try
            {
                logger.Info("Method: FioranoGetAccountFullInfo .  " + "RequestParam: " + accountNumber);
                var client = new RestClient(FioranoBaseUrl + "/EacbsEnquiry/GetAccountFullInfo/" + accountNumber);
                var request = new RestRequest(Method.GET);
                IRestResponse response = await client.ExecuteTaskAsync(request);

                fioranoFtResponse = JsonConvert.DeserializeObject<GetAccountFullInfoResponse>(response.Content);
                logger.Info("Method: FioranoGetAccountFullInfo .  " + "RequestParam: " + PostParam + "Response:" + fioranoFtResponse.BankAccountFullInfo.CUS_SHO_NAME + "||" + fioranoFtResponse.BankAccountFullInfo.NUBAN);
            }
            catch (Exception ex)
            {
                logger.Error("Method: FioranoGetAccountFullInfo Exception. Request " + "RequestParam: " + PostParam + " .ErrorMessage: " + ex.Message + "StackTrace: " + ex.StackTrace);
            }
            return fioranoFtResponse;
        }

        public class GetAccountFullInfoResponse
        {
            public BankAccountFullInfoNew BankAccountFullInfo { get; set; }
        }
        public class BankAccountFullInfoNew
        {
            public string NUBAN { get; set; }
            public string BRA_CODE { get; set; }
            public string DES_ENG { get; set; }
            public string CUS_NUM { get; set; }
            public string CUR_CODE { get; set; }
            public string LED_CODE { get; set; }
            public object SUB_ACCT_CODE { get; set; }
            public string CUS_SHO_NAME { get; set; }
            public string AccountGroup { get; set; }
            public string CustomerStatus { get; set; }
            public string ADD_LINE1 { get; set; }
            public string ADD_LINE2 { get; set; }
            public string MOB_NUM { get; set; }
            public string email { get; set; }
            public string ACCT_NO { get; set; }
            public string MAP_ACC_NO { get; set; }
            public string ACCT_TYPE { get; set; }
            public string ISO_ACCT_TYPE { get; set; }
            public string TEL_NUM { get; set; }
            public string DATE_OPEN { get; set; }
            public string STA_CODE { get; set; }
            public string CLE_BAL { get; set; }
            public string CRNT_BAL { get; set; }
            public object BAL_LIM { get; set; }
            public string TOT_BLO_FUND { get; set; }
            public object INTRODUCER { get; set; }
            public string DATE_BAL_CHA { get; set; }
            public string NAME_LINE1 { get; set; }
            public string NAME_LINE2 { get; set; }
            public string BVN { get; set; }
            public string REST_FLAG { get; set; }
            public RESTRICTIONS RESTRICTIONS { get; set; }
            public string IsSMSSubscriber { get; set; }
            public string Alt_Currency { get; set; }
            public string Currency_Code { get; set; }
            public string T24_BRA_CODE { get; set; }
            public string T24_CUS_NUM { get; set; }
            public string T24_CUR_CODE { get; set; }
            public string T24_LED_CODE { get; set; }
            public string OnlineActualBalance { get; set; }
            public string OnlineClearedBalance { get; set; }
            public string OpenActualBalance { get; set; }
            public string OpenClearedBalance { get; set; }
            public string WorkingBalance { get; set; }
            public string CustomerStatusCode { get; set; }
            public string CustomerStatusDeecp { get; set; }
            public object LimitID { get; set; }
            public string LimitAmt { get; set; }
            public string MinimumBal { get; set; }
            public string UsableBal { get; set; }
            public string AccountDescp { get; set; }
            public string CourtesyTitle { get; set; }
            public string AccountTitle { get; set; }
            public object AMFCharges { get; set; }

        }


        public class RESTRICTIONS
        {
            public List<RESTRICTION> RESTRICTION { get; set; }
        }

        public class RESTRICTION
        {
            [JsonProperty(PropertyName = "RESTRICTION.CODE")]
            public object RESTRICTIONCODE { get; set; }

            [JsonProperty(PropertyName = "RESTRICTION.DESCRIPTION")]
            public object RESTRICTIONDESCRIPTION { get; set; }

        }
    }
}
