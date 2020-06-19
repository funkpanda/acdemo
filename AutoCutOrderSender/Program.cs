using Dapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;

namespace AutoCutOrderSender
{
    class Program
    {
        static int Main(string[] args)
        {

            var dalton = args.Length > 0 ? args[0] : string.Empty;
            var temporaryDB = args.Length > 1 ? args[1] : string.Empty;

            var ok = true;
            if (string.IsNullOrWhiteSpace(dalton))
            {
                Console.WriteLine("Dalton url missing.");
                ok = false;
            }

            if (string.IsNullOrWhiteSpace(temporaryDB))
            {
                Console.WriteLine("temporaryDB connection string missing.");
                ok = false;
            }

            if (!ok)
            {
                Console.WriteLine("Required parameters: [Dalton url] [temporaryDB connection string]");
                return 1;
            }


            var ac = new AutoCutOrderProcessor(daltonUrl: dalton, temporaryDBConnectionString: new SqlConnection(temporaryDB));
            ac.AutoCutTableRead();

            //******Exit key for dev purpose***********
            //Console.ReadKey();
            //*****************************************
            return 0;
        }
    }

    class AutoCutOrderProcessor
    {
        string _dalton;
        SqlConnection _temporaryDB;
        public AutoCutOrderProcessor(string daltonUrl, SqlConnection temporaryDBConnectionString)
        {
            _dalton = daltonUrl;
            _temporaryDB = temporaryDBConnectionString;
        }
        public void AutoCutTableRead()
        {
            var query = "SELECT * FROM AutoCutOrders WHERE OrderState IS NULL";
            var results = _temporaryDB.Query<AutoCutOrder>(query).ToList();
            results.ForEach(ProcessOrder);
        }
         public void CancelOpenOrders()
        {
            SqlCommand cmd = new SqlCommand("MonexMargin_CancelOpenOrders", _temporaryDB);
            cmd.CommandType = CommandType.StoredProcedure;
            _temporaryDB.Open();
            cmd.ExecuteReader();
            _temporaryDB.Close();
        }

        public void UpdateQuantity()
        {
            SqlCommand cmd = new SqlCommand("MonexMargin_UpdateAutoCutPositionQuantity", _temporaryDB);
            cmd.CommandType = CommandType.StoredProcedure;
            _temporaryDB.Open();
            cmd.ExecuteReader();
            _temporaryDB.Close();
        }

        private void ProcessOrder(AutoCutOrder order)
        {
            if (order.SkipPosition)
            {
                SetOrderSkipped(order);
            }
            else
            {
                PostOrder(order);
            }
        }

        public void PostOrder(AutoCutOrder order)
        {
            var daltonOrder = new DaltonOrder
            {
                AccountPk = order.AccountPk,
                FDCNID = order.FDCNID,
                ClearingID = order.ClearingID,
                BatchID = order.BatchID,
                ClientTag = order.ClientTag,

                Legs = new List<Leg> { new Leg
                    {
                        Quantity = order.PositionQuantity,
                        Symbol = order.Symbol,
                    } }
            };
            var json = JsonConvert.SerializeObject(daltonOrder);

            WebClient client = new WebClient();
            client.Headers.Add("authorization", "d2lzaElIYWREaXJlY3RDb25uZWN0");
            client.Headers.Add("content-type", "application/json");


            var response = client.UploadString(_dalton, json);
            //**********Echo response for dev purpose************
            //Console.WriteLine(json);
            //Console.WriteLine(response);
            //***************************************************
            SetOrderSent(order);
        }

        public void SetOrderSent(AutoCutOrder order)
        {
            var query =
            @"UPDATE AutoCutOrders
              SET TimeSent=getutcdate(), 
                  OrderSent=1, 
                  ClientTag=@ClientTag,
                  OrderState='SENT'
              WHERE AutoCutOrdersID=@AutoCutOrdersID";
            _temporaryDB.Execute(query, order);
        }

        public void SetOrderSkipped(AutoCutOrder order)
        {
            var query =
            @"UPDATE AutoCutOrders
              SET OrderState='SKIPPED'
              WHERE AutoCutOrdersID=@AutoCutOrdersID";
            _temporaryDB.Execute(query, order);
        }
    }

    public class AutoCutOrder
    {
        public int AutoCutOrdersID { get; set; }
        public int AccountPk { get; set; }
        public string Symbol { get; set; }
        public int PositionQuantity { get; set; }
        public bool SkipPosition { get; set; }
        public int ClearingID { get; set; }
        public int FDCNID { get; set; }
        public string ClientTag { get; set; } = Guid.NewGuid().ToString("N").ToLower();
        public string BatchID { get; set; }
    }

    public class DaltonOrder
    {
        public int AccountPk { get; set; }
        public int ClearingID { get; set; }
        public int FDCNID { get; set; }
        public string BatchID { get; set; }
        public string ClientTag { get; set; }
        public string Source { get; set; } = "Monex_AutoCut";

        public List<Leg> Legs { get; set; }
    }

    public class Leg
    {
        public int Quantity { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
    }

}
