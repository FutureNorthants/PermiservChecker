using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Text;

namespace PermiservChecker.Helpers
{
    class CXMFieldUpdate
    {
        public CXMFieldUpdate(String caseNumber, String message, String cxmEndPoint, String cxmAPIKey, ILogger log)
        {
            string data = "{\"" + "permiserv-updates" + "\":\"" + message + "\"}";
            string url = cxmEndPoint + "/api/service-api/norbert/case/" + caseNumber + "/edit?key=" + cxmAPIKey;
            Encoding encoding = Encoding.Default;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "PATCH";
            request.ContentType = "application/json; charset=utf-8";
            byte[] buffer = encoding.GetBytes(data);
            Stream dataStream = request.GetRequestStream();
            dataStream.Write(buffer, 0, buffer.Length);
            dataStream.Close();
            try
            {
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                string result = "";
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), System.Text.Encoding.Default))
                {
                    result = reader.ReadToEnd();
                }
            }
            catch (Exception error)
            {
                log.LogError(caseNumber + " : " + error.ToString());
                log.LogError(caseNumber + " : Error updating CXM field " + "permiserv-updates" + " with message : " + message);
            }
        }
    }
}
