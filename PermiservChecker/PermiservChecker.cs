using System;
using System.Net.Http;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace PermiservChecker
{
    public static class PermiservChecker
    {
        [FunctionName("PermiservChecker")]
        //public static void Run([TimerTrigger("0 * * * *")]TimerInfo myTimer, ILogger log)
        public static void Run([TimerTrigger("* * * * *")]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"PermiservChecker executed at: {DateTime.Now}");
            String[] cases = null;
            String filter = "gws-with-permiserv";
            int pages = 0;
            var keyClient = new SecretClient(new Uri("https://permiservchecker.vault.azure.net/"), new DefaultAzureCredential());
            KeyVaultSecret secret = null;
            secret = keyClient.GetSecret("cxmEndpoint");
            String cxmEndpoint = secret.Value;
            secret = keyClient.GetSecret("cxmAPIKey");
            String cxmAPIKey = secret.Value;
            secret = keyClient.GetSecret("permiservEndpoint");
            String permiservEndpoint = secret.Value;
            secret = keyClient.GetSecret("permiservAPIKey");
            String permiservAPIKey = secret.Value;

            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(cxmEndpoint);
            string requestParameters = "key=" + cxmAPIKey + "&page=" + "1";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/service-api/norbert/filters/" + filter + "/summaries" + "?" + requestParameters);
            try
            {
                HttpResponseMessage response = client.SendAsync(request).Result;
                if (response.IsSuccessStatusCode)
                {
                    HttpContent responseContent = response.Content;
                    String responseString = responseContent.ReadAsStringAsync().Result;
                    JObject caseSearch = JObject.Parse(responseString);
                    int totalCases = (int)caseSearch.SelectToken("total");
                    pages = totalCases / 15;
                }
                else
                {
                    log.LogError("CXM API get filtered cases (" + filter + ") failed");
                }
            }
            catch (Exception error)
            {
                log.LogError("CXM API get filtered cases (" + filter + ") exception " + error.Message);
            }

            requestParameters = "key=" + cxmAPIKey + "&page=" + pages.ToString();
            request = new HttpRequestMessage(HttpMethod.Get, "/api/service-api/norbert/filters/" + filter + "/summaries" + "?" + requestParameters);

            try
            {
                HttpResponseMessage response = client.SendAsync(request).Result;
                if (response.IsSuccessStatusCode)
                {
                    HttpContent responseContent = response.Content;
                    String responseString = responseContent.ReadAsStringAsync().Result;
                    JObject caseSearch = JObject.Parse(responseString);
                    int totalCases = (int)caseSearch.SelectToken("total");
                    int numOfCases = (int)caseSearch.SelectToken("num_items");
                    cases = new String[numOfCases];
                    if (totalCases == 0)
                    {
                        log.LogInformation("No cases to be processed");
                    }
                    else
                    {
                        for (int currentCase = 0; currentCase < numOfCases; currentCase++)
                        {
                            cases[currentCase] = (String)caseSearch.SelectToken("items[" + currentCase + "].reference");               
                            log.LogInformation(cases[currentCase] + " loaded for processing");
                        }
                    }
                }
                else
                {
                    log.LogError("CXM API get filtered cases (" + filter + ") failed");
                }
            }
            catch (Exception error)
            {
                log.LogError("CXM API get filtered cases (" + filter + ") error : " + error.Message);
            }

            try
            {
                client = new HttpClient();
                client.BaseAddress = new Uri(permiservEndpoint);
                for (int currentCase = 0; currentCase < cases.Length; currentCase++)
                {
                    requestParameters = "apiKey=" + permiservAPIKey + "&dataType=" + "get" + "&callType=" + "search" + "&councilJobNumber=" + cases[currentCase];
                    request = new HttpRequestMessage(HttpMethod.Get, "/api/" + "?" + requestParameters);
                    log.LogInformation(cases[currentCase] + " checking status");
                    HttpResponseMessage response = client.SendAsync(request).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        HttpContent responseContent = response.Content;
                        String responseString = responseContent.ReadAsStringAsync().Result;
                        JObject caseSearch = JObject.Parse(responseString);
                        int numRows = 0;
                        if (caseSearch.SelectToken("Success").ToString().Equals("true"))
                        {                      
                           numRows = (int)caseSearch.SelectToken("['Num Rows']");

                            for (int currentRow = 0; currentRow < numRows; currentRow++)
                            {
                             if (caseSearch.SelectToken("Result[" + currentRow + "].state").ToString().Equals("Dispatched"))
                                {

                                }
                            if (caseSearch.SelectToken("Result[" + currentRow + "].state").ToString().Equals("Replaced"))
                                {

                                }
                            }
                        }
                        else
                        {
                            log.LogError("Permiserv API case search for " + cases[currentCase] +  " error : " + caseSearch.SelectToken("Errors[" + currentCase + "]"));
                        }
                    }
                }
            }
            catch (Exception error)
            {
                log.LogError("Permiserv API get error " + error.Message);
            }
            
        }
    }
}
