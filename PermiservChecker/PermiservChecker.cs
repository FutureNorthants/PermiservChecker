using System;
using System.Net.Http;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PermiservChecker.Helpers;

namespace PermiservChecker
{
    public static class PermiservChecker
    {
        [FunctionName("PermiservChecker")]
        public static void Run([TimerTrigger("0 0 0 * * *")]TimerInfo myTimer, ILogger log)
        //public static void Run([TimerTrigger("0 * * * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"PermiservChecker v1.7 executed at: {DateTime.Now}");
            int caseCount = 0;
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

            HttpClient cxmClient = new HttpClient();
            cxmClient.BaseAddress = new Uri(cxmEndpoint);
            string requestParameters = "key=" + cxmAPIKey + "&page=" + "1";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/service-api/norbert/filters/" + filter + "/summaries" + "?" + requestParameters);
            try
            {
                HttpResponseMessage response = cxmClient.SendAsync(request).Result;
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


            int casesTransitioned = 0;
            for(int currentPage = pages; currentPage > 0; currentPage--)
            {
                requestParameters = "key=" + cxmAPIKey + "&page=" + currentPage.ToString();
                request = new HttpRequestMessage(HttpMethod.Get, "/api/service-api/norbert/filters/" + filter + "/summaries" + "?" + requestParameters);

                try
                {
                    HttpResponseMessage response = cxmClient.SendAsync(request).Result;
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
                    HttpClient permiservClient = new HttpClient();
                    permiservClient.BaseAddress = new Uri(permiservEndpoint);
                    for (int currentCase = 0; currentCase < cases.Length; currentCase++)
                    {
                        requestParameters = "apiKey=" + permiservAPIKey + "&dataType=" + "get" + "&callType=" + "search" + "&councilJobNumber=" + cases[currentCase];
                        request = new HttpRequestMessage(HttpMethod.Get, "/api/" + "?" + requestParameters);
                        HttpResponseMessage response = permiservClient.SendAsync(request).Result;
                        if (response.IsSuccessStatusCode)
                        {
                            HttpContent responseContent = response.Content;
                            String responseString = responseContent.ReadAsStringAsync().Result;
                            JObject caseSearch = JObject.Parse(responseString);
                            int numRows = 0;
                            if (caseSearch.SelectToken("Success").ToString().Equals("true"))
                            {
                                numRows = (int)caseSearch.SelectToken("['Num Rows']");
                                if (numRows == 0)
                                {
                                    requestParameters = "key=" + cxmAPIKey;
                                    request = new HttpRequestMessage(HttpMethod.Post, "/api/service-api/norbert/case/" + cases[currentCase] + "/transition/" + "with-veolia" + "?" + requestParameters);
                                    try
                                    {
                                        response = cxmClient.SendAsync(request).Result;
                                        if (response.IsSuccessStatusCode)
                                        {
                                            log.LogWarning("Permiserv record not found, transitioning " + cases[currentCase] + " back to with-veolia for resubmission");
                                            casesTransitioned++;
                                        }
                                        else
                                        {
                                            log.LogError("Permiserv transition error " + cases[currentCase] + " error : Unsuccessful status code");
                                        }
                                    }
                                    catch (Exception error)
                                    {
                                        log.LogError("Permiserv transition error " + cases[currentCase] + " error : " + error.Message);
                                    }
                                }

                                for (int currentRow = 0; currentRow < numRows; currentRow++)
                                {
                                    caseCount++;
                                    CXMFieldUpdate updater = new CXMFieldUpdate(cases[currentCase], caseSearch.SelectToken("Result[" + currentRow + "].state").ToString(), cxmEndpoint, cxmAPIKey, log);
                                    if (caseSearch.SelectToken("Result[" + currentRow + "].state").ToString().Equals("Dispatched") ||
                                        caseSearch.SelectToken("Result[" + currentRow + "].state").ToString().Equals("Replaced"))
                                    {
                                        requestParameters = "key=" + cxmAPIKey;
                                        request = new HttpRequestMessage(HttpMethod.Post, "/api/service-api/norbert/case/" + cases[currentCase] + "/transition/" + "active-subscription" + "?" + requestParameters);
                                        try
                                        {
                                            response = cxmClient.SendAsync(request).Result;
                                            if (response.IsSuccessStatusCode)
                                            {
                                                log.LogInformation(caseCount + " : " + cases[currentCase] + " transitioned to active-subscription");
                                                casesTransitioned++;                                               
                                            }
                                            else
                                            {
                                                log.LogError(caseCount + " : " + "Permiserv transition error " + cases[currentCase] + " error : Unsuccessful status code");
                                            }
                                        }
                                        catch (Exception error)
                                        {
                                            log.LogError(caseCount + " : " + "Permiserv transition error " + cases[currentCase] + " error : " + error.Message);
                                        }
                                    }
                                    else
                                    {
                                        log.LogInformation(caseCount + " : " + cases[currentCase] + " ignored - Permiserv status is : " + caseSearch.SelectToken("Result[" + currentRow + "].state").ToString());
                                    }
                                                                    }
                            }
                            else
                            {
                                log.LogError("Permiserv API case search for " + cases[currentCase] + " error : " + caseSearch.SelectToken("Errors[" + currentCase + "]"));
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
}
