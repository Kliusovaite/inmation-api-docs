﻿using inmation.api.history;
using inmation.api.model;
using inmation.api.model.rpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace inmation.api.client.example.ReadRawHistoricalData
{
    class Program
    {
        private const string WebSocketUrl = "ws://localhost:8002/ws";
        private const string Username = "USERNAME";
        private const string Password = "PASSWORD";
        private static Client _client;

        private static DateTime _startTime = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static int _numberOfSamples = 7;
        private static int _intervalInMilliseconds = 1000;

        static void Main(string[] args)
        {
            // Make sure the following Variable / Holder items exist in the system and are archived
            List<Identity> identityList = new List<Identity>();
            identityList.Add(new Identity("/System/Core/Examples/Demo Data/Process Data/DC4711"));
            identityList.Add(new Identity("/System/Core/Examples/Demo Data/Process Data/DC666"));

            // Create the API client
            _client = CreateApiClient(Username, Password).Result;

            // Write Items / generate historical data
            List<ItemValue> writeItems = new List<ItemValue>();
            foreach (Identity identity in identityList)
            {
                for (int i = 0; i < _numberOfSamples; i++)
                {
                    ItemValue itemValue = new ItemValue(identity.Path, i * 10, 0, _startTime.AddMilliseconds(i * _intervalInMilliseconds));
                    writeItems.Add(itemValue);
                }
            }

            WriteMultipleItemsAtOnce(writeItems).Wait();

            // Read Raw Historical Data
            ReadRawHistoricalData(identityList).Wait();

            // Read Filtered Raw Historical Data (with LINQ)
            ReadRawHistoricalData_Filtered(identityList).Wait();

            Console.ReadLine();
            _client.Dispose();
        }

        /// <summary>
        /// Reads raw historical data
        /// </summary>
        /// <param name="items">List of Identity instances to read.</param>
        private static async Task ReadRawHistoricalData(List<Identity> items)
        {
            LogResult();

            DateTime startTime = _startTime;
            DateTime endTime = startTime.AddMilliseconds(_numberOfSamples * _intervalInMilliseconds);

            RawHistoricalDataResponse readRawHistoricalDataResponse = await _client.ReadRawHistoricalDataAsync(items, startTime, endTime);

            if (readRawHistoricalDataResponse.Error != null)
            {
                Console.WriteLine(string.Format("An error has occurred : {0}", readRawHistoricalDataResponse.Error?.First().Message));
            }
            else if (readRawHistoricalDataResponse.Data != null)
            {
                // In this case we got raw historical data 
                // Iterate through the result by using iterators (which make use of lazy loading) and output the itemValues.
                RawHistoricalData rawHistoricalData = readRawHistoricalDataResponse.Data;
                foreach (RawHistoricalDataQueryData queryData in rawHistoricalData.QueryDataIterator())
                {
                    foreach (RawHistoricalDataItemData item in queryData.ItemsIterator())
                    {
                        foreach (RawHistoricalDataItemValue itemValue in item.ItemValueIterator())
                        {
                            Console.WriteLine("ItemValue: {0}", itemValue);
                        }
                    }
                }
            }
            else if (readRawHistoricalDataResponse.Strategy != null)
            {
                // In case the size of the data exceeds the 'query_count_limit' a query strategy is returned to fetch the 
                // historical data in multiple chunks.
                Console.WriteLine("A strategy is returned by the server, which will be described in an other example.");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Reads raw historical data by providing a filter expression defined by a LINQ query.
        /// </summary>
        /// <param name="items">List of Identity instances to read.</param>
        private static async Task ReadRawHistoricalData_Filtered(List<Identity> items)
        {
            LogResult();

            DateTime startTime = _startTime;
            DateTime endTime = startTime.AddMilliseconds(_numberOfSamples * _intervalInMilliseconds - 2);

            // Create an instance of a 'RawHistoryContext'.
            RawHistoryContext rawHistoryContext = new RawHistoryContext();

            string pathSecondItem = items.Skip(1).First().Path;

            // Define a LINQ 'where' query and call the (extension) method 'SetFilter'. This method requires the using 'inmation.api.history;'.
            rawHistoryContext.Where(
            n => (n.Path.EndsWith("Item100") && n.ValueAsDouble > 10 && n.ValueAsDouble < 41)
                    || (n.Path.Equals(pathSecondItem) && n.ValueAsDouble > 40 && n.ValueAsDouble < 61)
                    || (n.QualityText.Equals("Bad") || n.Timestamp.Equals(startTime))).SetFilter();

            // Define the options
            ReadRawHistoricalDataOptions options = new ReadRawHistoricalDataOptions();
            options.Bounds = false;
            options.Fields = new List<string>() { "ALL" };

            // Fetch historical data by using the 'ReadRawHistoricalData' method of the RawHistoryContext class.
            // The filter will be applied on the raw historical data retrieved for the items within the provided interval.
            RawHistoricalDataResponse readRawHistoricalDataResponse = await rawHistoryContext.ReadRawHistoricalData(_client, items, startTime, endTime, options);

            if (readRawHistoricalDataResponse.Error != null)
            {
                Console.WriteLine(string.Format("An error has occurred : {0}", readRawHistoricalDataResponse.Error?.First().Message));
            }
            else if (readRawHistoricalDataResponse.Data != null)
            {
                // In this case we got raw historical data 
                // Iterate through the result by using iterators (which make use of lazy loading) and output the itemValues.
                RawHistoricalData rawHistoricalData = readRawHistoricalDataResponse.Data;
                foreach (RawHistoricalDataQueryData queryData in rawHistoricalData.QueryDataIterator())
                {
                    foreach (RawHistoricalDataItemData item in queryData.ItemsIterator())
                    {
                        foreach (RawHistoricalDataItemValue itemValue in item.ItemValueIterator())
                        {
                            Console.WriteLine("ItemValue: {0}", itemValue);
                        }
                    }
                }

                // Calculate the total duration for item with path <pathSecondItem>.
                long? totalDuration = rawHistoricalData.QueryData.Sum(n => n.Items.Where(i => i.Path.Equals(pathSecondItem)).Sum(o => o.SummarizeDuration()));
                Console.WriteLine("\nTotal duration for Item '{0}': {1}", pathSecondItem, totalDuration);
            }
            else if (readRawHistoricalDataResponse.Strategy != null)
            {
                // In case the size of the data exceeds the 'query_count_limit' a query strategy is returned to fetch the 
                // historical data in multiple chunks.
                Console.WriteLine("A strategy is returned by the server, which will be described in an other example.");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Writes multiple items in one single request.
        /// Currently only writing based on item path and VQT is supported.
        /// </summary>
        /// <param name="items">List of ItemValue instances to write.</param>
        private static async Task WriteMultipleItemsAtOnce(List<ItemValue> items)
        {
            LogResult();
            WriteResponse writeResponse = await _client.WriteAsync(items);

            if (writeResponse.Error != null)
            {
                Console.WriteLine(string.Format("An error has occurred : {0}", writeResponse.Error?.First().Message));
            }
            else
            {
                foreach (ItemValue itemValue in writeResponse.Data)
                {
                    Console.WriteLine("ItemValue: {0}", itemValue);
                }
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Creates a connected client. 
        /// In case credentials are provided, the user will be authenticated and the credentials will be stored in the session.
        /// By providing credentials in the constructor of the apiClient it is not necessary to provide credentials for each individual request.
        /// during the session.
        /// </summary>
        private static async Task<Client> CreateApiClient(string username = null, string password = null)
        {
            Client apiClient = new Client();
            try
            {
                apiClient.OnConnectionChanged += OnConnectionStateChanged;
                apiClient.OnError += OnError;

                ConnectOptions options = new ConnectOptions(username, password, true);
                // Connect and authenticate. By providing credentials to the connectWs method, the credentials will be stored in the session.
                ConnectionResponse connectResponse = await apiClient.ConnectWs(WebSocketUrl, options);
                if (connectResponse.Error != null)
                {
                    Console.WriteLine("Connect failed: {0}", connectResponse.Error?.First().Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred during client creation: {0}", ex.Message);
            }
            Console.WriteLine("");
            return apiClient;
        }

        private static void LogResult([System.Runtime.CompilerServices.CallerMemberName] string callingMethodName = "")
        {
            Console.WriteLine("Result of {0}:", callingMethodName);
        }

        #region EventHandlers

        private static void OnOpen()
        {
            Console.WriteLine("WebSocket connection opened!");
        }

        private static void OnConnectionStateChanged(WsConnectionInfo connectionInfo)
        {
            Console.WriteLine(connectionInfo);
        }

        private static void OnError(ApiErrorResponse errorResponse, long? requestId)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Error error in errorResponse.ErrorList)
            {
                sb.AppendLine(error.ToString());
                Console.WriteLine(error);
            }
        }

        private static void OnClose(bool wasClean, ApiErrorResponse errorResponse)
        {
            if (!wasClean)
            {
                Console.WriteLine("Connection is forcibly closed.");
                OnError(errorResponse, null);
            }
            Console.WriteLine("Connection is closed.");
        }

        #endregion
    }
}
