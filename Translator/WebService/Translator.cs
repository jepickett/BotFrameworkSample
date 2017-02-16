using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Net;
using System.IO;
using System.Threading;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Net.Cache;
using System.Runtime.InteropServices;

namespace TranslatorBot
{
    public class Translator
    {
        // Get Client Id and Client Secret from https://datamarket.azure.com/developer/applications/
        const string clientID     = "ClientID";
        const string clientSecret = "ClientSecret";

        public string Translate(string text, string sourceLanguage, string destinationLanguage)
        {
            try
            {
                using (DatamarketAuthenticator datamarketAuthenticator = new DatamarketAuthenticator(clientID, clientSecret))
                {
                    DatamarketAccessToken accessToken;
                    string authToken;

                    accessToken = datamarketAuthenticator.GetAccessToken();
                    authToken = "Bearer " + accessToken.access_token;
                    string response = SendTranslateRequest(authToken, text, sourceLanguage, destinationLanguage);
                    return response;
                }
            }
            catch (WebException e)
            {
                return ProcessWebException(e);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private string SendTranslateRequest(string authToken, string text, string sourceLanguage, string destinationLanguage)
        {
            string uri = "http://api.microsofttranslator.com/v2/Http.svc/Translate?text=" + System.Web.HttpUtility.UrlEncode(text) + "&from=" + sourceLanguage + "&to=" + destinationLanguage;
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
            httpWebRequest.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            httpWebRequest.Headers.Add("Authorization", authToken);
            string translation = "";
            try
            {
                using (WebResponse response = httpWebRequest.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        System.Runtime.Serialization.DataContractSerializer dcs = new System.Runtime.Serialization.DataContractSerializer(Type.GetType("System.String"));
                        translation = (string)dcs.ReadObject(stream);
                    }
                }
            }
            catch
            {
                throw;
            }

            return translation;
        }

        private string ProcessWebException(WebException e)
        {
            string strResponse = string.Empty;
            using (HttpWebResponse response = (HttpWebResponse)e.Response)
            {
                using (Stream responseStream = response.GetResponseStream())
                {
                    using (StreamReader sr = new StreamReader(responseStream, System.Text.Encoding.ASCII))
                    {
                        strResponse = sr.ReadToEnd();
                    }
                }
            }
            return string.Format("Http status code={0}, error message={1}", e.Status, strResponse);
        }
    }

    [DataContract]
    public class DatamarketAccessToken
    {
        [DataMember]
        public string access_token { get; set; }
        [DataMember]
        public string token_type { get; set; }
        [DataMember]
        public string expires_in { get; set; }
        [DataMember]
        public string scope { get; set; }
    }

    public class DatamarketAuthenticator : IDisposable
    {
        public static readonly string DatamarketAccessUri = "https://datamarket.accesscontrol.windows.net/v2/OAuth2-13";
        private string clientId;
        private string clientSecret;
        private string request;
        private DatamarketAccessToken token;
        private Timer accessTokenRenewer;
        private const int RefreshTokenDuration = 9;

        // Obtain AccessToken using method at http://msdn.microsoft.com/en-us/library/hh454950.aspx 
        public DatamarketAuthenticator(string clientId, string clientSecret)
        {
            this.clientId = clientId;
            this.clientSecret = clientSecret;

            this.request = string.Format("grant_type=client_credentials&client_id={0}&client_secret={1}&scope=http://api.microsofttranslator.com", HttpUtility.UrlEncode(clientId), HttpUtility.UrlEncode(clientSecret));
            this.token = HttpPost(DatamarketAccessUri, this.request);

            accessTokenRenewer = new Timer(new TimerCallback(OnTokenExpiredCallback), this, TimeSpan.FromMinutes(RefreshTokenDuration), TimeSpan.FromMilliseconds(-1));
        }

        public DatamarketAccessToken GetAccessToken()
        {
            return this.token;
        }

        private void RenewAccessToken()
        {
            DatamarketAccessToken newAccessToken = HttpPost(DatamarketAccessUri, this.request);
            this.token = newAccessToken;
            System.Diagnostics.Debugger.Log(1, "failure", string.Format("Renewed token for user: {0} is: {1}", this.clientId, this.token.access_token));
        }

        private void OnTokenExpiredCallback(object stateInfo)
        {
            try
            {
                RenewAccessToken();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debugger.Log(1, "failure", string.Format("Failed renewing access token. Details: {0}", ex.Message));
            }
            finally
            {
                try
                {
                    accessTokenRenewer.Change(TimeSpan.FromMinutes(RefreshTokenDuration), TimeSpan.FromMilliseconds(-1));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debugger.Log(1, "failure", string.Format("Failed to reschedule the timer to renew access token. Details: {0}", ex.Message));
                }
            }
        }

        private DatamarketAccessToken HttpPost(string DatamarketAccessUri, string requestDetails)
        {
            WebRequest webRequest = WebRequest.Create(DatamarketAccessUri);
            webRequest.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);

            webRequest.ContentType = "application/x-www-form-urlencoded";
            webRequest.Method = "POST";
            byte[] bytes = Encoding.ASCII.GetBytes(requestDetails);
            webRequest.ContentLength = bytes.Length;
            using (Stream outputStream = webRequest.GetRequestStream())
            {
                outputStream.Write(bytes, 0, bytes.Length);
            }
            using (WebResponse webResponse = webRequest.GetResponse())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(DatamarketAccessToken));
                DatamarketAccessToken token = (DatamarketAccessToken)serializer.ReadObject(webResponse.GetResponseStream());
                return token;
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; 

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    accessTokenRenewer.Dispose();
                    accessTokenRenewer = null;
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
