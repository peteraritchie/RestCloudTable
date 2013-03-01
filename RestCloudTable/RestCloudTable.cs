using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.OData;
using Microsoft.WindowsAzure.Storage.Core;
using Microsoft.WindowsAzure.Storage.Table;
using ServerCertificateValidationCallback = System.Func<System.Security.Cryptography.X509Certificates.X509Chain,
			System.Net.Security.SslPolicyErrors, object,
			System.Security.Cryptography.X509Certificates.X509Certificate,
			System.Net.HttpWebRequest, System.Net.Security.RemoteCertificateValidationCallback, bool>;

namespace PRI
{
	public class RestCloudTable
	{
		private readonly string accountName;
		private readonly string accountKey;
		private readonly string tableName;
		private const string XMsVersion = "2012-02-12";
		private const string UserAgent = "PRI-test/0.0.1";
		private const string TableUriFormatString = "https://{0}.table.core.windows.net/{1}";

		public RestCloudTable(string accountName, string accountKey, string tableName)
		{
			this.accountName = accountName;
			this.accountKey = accountKey;
			this.tableName = tableName;
			Timeout = 90;
		}

		public int Timeout { get; set; }

		#region utility

		private static IEnumerable<T> ToEntities<T>(XmlDocument doc) where T : TableEntity, new()
		{
			if(doc == null) return new List<T>();

			XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
			manager.AddNamespace("atom", "http://www.w3.org/2005/Atom"); 
			XmlNodeList nodes = doc.SelectNodes("atom:feed/atom:entry", manager);

			if (nodes == null) return new List<T>();

			List<T> list = new List<T>(nodes.Count);
			list.AddRange(from XmlNode xn in nodes select ToEntity<T>(XElement.Parse(xn.OuterXml)));
			return list;
		}

		private static T ToEntity<T>(XElement doc) where T : TableEntity, new()
		{
			if (doc == null) return null;
			var properties = from property in doc.Descendants()
			                 where property.Name.Namespace == "http://schemas.microsoft.com/ado/2007/08/dataservices"
			                 select property;
			var entity = new T
			             {
			             	PartitionKey = properties.First(e => e.Name.LocalName == "PartitionKey").Value,
			             	RowKey = properties.First(e => e.Name.LocalName == "RowKey").Value,
							ETag = properties.Any(e=>e.Name.LocalName == "ETag") ? 
								properties.First(e=>e.Name.LocalName == "ETag").Value : null,
			             };
			var type = entity.GetType();
			foreach (
				var prop in
					type.GetProperties(BindingFlags.SetField | BindingFlags.Public | BindingFlags.Instance
					| BindingFlags.DeclaredOnly))
			{
				var xElement =
					properties.FirstOrDefault(
						value => 0 == String.Compare(value.Name.LocalName, prop.Name, 
							StringComparison.OrdinalIgnoreCase));
				if (null != xElement)
				{
					var c = TypeDescriptor.GetConverter(prop.PropertyType);
					if (c.CanConvertFrom(typeof (String)) && c.CanConvertTo(prop.PropertyType))
					{
						prop.SetValue(entity, c.ConvertFromInvariantString(xElement.Value), null);
					}
				}
			}
			return entity;
		}

		private static void SignRequest(string accountName, string accountKey, HttpWebRequest request)
		{
			var resource = request.RequestUri.PathAndQuery;
			if (resource.Contains("?"))
			{
				resource = resource.Substring(0, resource.IndexOf("?", StringComparison.Ordinal));
			}
			request.Headers.Add("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));

			string authorizationHeader = CreateLiteAuthorizationHeader(request, resource,
			                                                           accountName, accountKey);
			request.Headers.Add("Authorization", authorizationHeader);
		}

		private static bool OnServerCertificateValidationCallback(object sender, X509Certificate certificate, 
			X509Chain chain, SslPolicyErrors errors)
		{
			if (errors == SslPolicyErrors.None) return true;
			if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
			{
				var effectiveDate = DateTime.Parse(certificate.GetEffectiveDateString());
				var expirationDate = DateTime.Parse(certificate.GetExpirationDateString());
				if (effectiveDate <= DateTime.Now && expirationDate.AddDays(-14) >= DateTime.Now)
				{
					return true;
				}
			}
			return false;
		}

		private static Dictionary<string, object> GetEntityProperties(TableEntity entity)
		{
			IEnumerable<PropertyInfo> properties = entity.GetType().GetProperties();
			return properties.Where(current => current.Name != "PartitionKey" && current.Name != "RowKey" 
				&& current.Name != "Timestamp" && current.Name != "ETag" && (current.GetSetMethod() != null) 
				&& current.GetSetMethod().IsPublic && (current.GetGetMethod() != null) 
				&& current.GetGetMethod().IsPublic)
				.ToDictionary(current => current.Name, current => current.GetValue(entity, null));
		}

		private static HttpWebRequest BuildRequestCore(Uri uri)
		{
			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
			request.Accept = "application/atom+xml,application/xml";
			request.Headers.Add("Accept-Charset", "UTF-8");
			request.Headers.Add("MaxDataServiceVersion", "2.0;NetFx");
			return request;
		}

		/// http://blog.einbu.no/2009/08/authenticating-against-azure-table-storage/
		/// http://social.msdn.microsoft.com/Forums/en-US/windowsazureconnectivity/thread/84415c36-9475-4af0-9f52-c534f5681432/
		private static string CreateLiteAuthorizationHeader(WebRequest request, string resource,
			string accountName, string key)
		{
			string signableText = String.Format("{0}\n/{1}{2}",
			                                    request.Headers["x-ms-date"],
			                                    accountName,
			                                    resource
				); string signature;
			using (HMACSHA256 hmacsha256 = new HMACSHA256(Convert.FromBase64String(key)))
			{
				byte[] data = Encoding.UTF8.GetBytes(signableText);
				signature = Convert.ToBase64String(hmacsha256.ComputeHash(data));
			}

			return String.Format(CultureInfo.InvariantCulture,
			                     "{0} {1}:{2}", "SharedKeyLite", accountName, signature);
		}

		private static string CreateAuthorizationHeader(WebRequest request, string resource,
			string accountName, string key)
		{
			var signableText = String.Format("{0}\n{1}\n{2}\n{3}\n/{4}/{5}", request.Method,
			                                 request.Headers["Content-MD5"],
			                                 request.Headers["Content-Type"],
			                                 request.Headers["x-ms-date"], accountName, resource);
			using (HMACSHA256 hasher = new HMACSHA256(Convert.FromBase64String(key)))
			{
				string signature = Convert.ToBase64String(hasher.ComputeHash(Encoding.UTF8.GetBytes(signableText)));

				return String.Format(CultureInfo.InvariantCulture,
				                     "{0} {1}:{2}", "SharedKeyLite", accountName, signature);
			}
		}

		private static Uri BuildQueryUri<T>(TableQuery<T> tableQuery, Uri tableUri) where T : ITableEntity, new()
		{
			UriQueryBuilder builder = new UriQueryBuilder();
			if (!String.IsNullOrWhiteSpace(tableQuery.FilterString)) builder.Add("$filter", tableQuery.FilterString);
			if (tableQuery.TakeCount.HasValue)
			{
				builder.Add("$top", tableQuery.TakeCount.Value.ToString(CultureInfo.InvariantCulture));
			}
			StringBuilder sb = new StringBuilder();
			if (tableQuery.SelectColumns != null && tableQuery.SelectColumns.Any())
			{
				sb.Append(tableQuery.SelectColumns.Aggregate((a, x) => a + "," + x));
				if (tableQuery.SelectColumns.All(e => e != "PartitionKey")) sb.Append(",PartitionKey");
				if (tableQuery.SelectColumns.All(e => e != "RowKey")) sb.Append(",RowKey");
				if (tableQuery.SelectColumns.All(e => e != "Timestamp")) sb.Append(",Timestamp");
				builder.Add("$select", sb.ToString());
			}

			return builder.AddToUri(tableUri);
		}

		private static string GetTableUrlText(string accountName, string tableName)
		{
			return String.Format(TableUriFormatString, accountName, tableName);
		}

		private static Stream GetRequestStreamForTableCreate(string tableName, HttpWebRequest request)
		{
			ODataMessageWriterSettings oDataMessageWriterSettings =
				new ODataMessageWriterSettings {CheckCharacters = false, Version = ODataVersion.V2};
			ODataRequestMessageAdapter oDataRequestMessageAdapter = new ODataRequestMessageAdapter(request);
			ODataMessageWriter oDataMessageWriter = new ODataMessageWriter(oDataRequestMessageAdapter,
			                                                               oDataMessageWriterSettings);
			ODataWriter writer = oDataMessageWriter.CreateODataEntryWriter();
			ODataEntry oDataEntry = new ODataEntry
			                        {
			                        	Properties = new List<ODataProperty>
			                        	             {
			                        		        	new ODataProperty {Name = "TableName", Value = tableName},
			                        		        }
			                        };

			writer.WriteStart(oDataEntry);
			writer.WriteEnd();
			writer.Flush();
			var stream = oDataRequestMessageAdapter.GetStream();
			return stream;
		}

		private static Stream GetRequestStreamForEntity<T>(T entity, HttpWebRequest request) where T : TableEntity
		{
			ODataMessageWriterSettings oDataMessageWriterSettings =
				new ODataMessageWriterSettings {CheckCharacters = false, Version = ODataVersion.V2};
			ODataRequestMessageAdapter oDataRequestMessageAdapter = new ODataRequestMessageAdapter(request);
			ODataMessageWriter oDataMessageWriter = new ODataMessageWriter(oDataRequestMessageAdapter,
			                                                               oDataMessageWriterSettings);
			ODataWriter writer = oDataMessageWriter.CreateODataEntryWriter();
			ODataEntry oDataEntry = new ODataEntry
			                        {
			                        	Properties =
			                        		GetEntityProperties(entity)
			                        		.Select(
			                        			pair => new ODataProperty {Name = pair.Key, Value = pair.Value})
			                        		.Concat(new List<ODataProperty>
			                        		        {
			                        		        	new ODataProperty {Name = "PartitionKey", Value = entity.PartitionKey},
			                        		        	new ODataProperty {Name = "RowKey", Value = entity.RowKey}
			                        		        })
			                        };

			writer.WriteStart(oDataEntry);
			writer.WriteEnd();
			writer.Flush();
			var stream = oDataRequestMessageAdapter.GetStream();
			return stream;
		}

		#endregion

		public static void Delete(string accountName, string accountKey, string tableName)
		{
			Uri uri = new Uri(String.Format(TableUriFormatString, accountName, 
				string.Format("Tables('{0}')", tableName)));
			var request = BuildRequestCore(uri);
			request.Method = "DELETE";
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			request.ContentLength = 0;
			SignRequest(accountName, accountKey, request);

			try
			{
				request.GetResponse();
			}
			catch (WebException webException)
			{
				var response = webException.Response as HttpWebResponse;
				if (response != null && response.StatusCode == HttpStatusCode.Conflict)
				{
					throw new TableAlreadyExistsException();
				}
				throw;
			}
		}

		public static RestCloudTable Create(string accountName, string accountKey, string tableName)
		{
			Uri uri = new Uri(String.Format(TableUriFormatString, accountName, "Tables"));
			var request = BuildRequestCore(uri);
			request.Method = WebRequestMethods.Http.Post;
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			var stream = GetRequestStreamForTableCreate(tableName, request);
			request.ContentLength = stream.Length;
			SignRequest(accountName, accountKey, request);
			var requestStream = request.GetRequestStream();
			stream.CopyTo(requestStream);
			requestStream.Close();
			try
			{
				request.GetResponse();
			}
			catch (WebException webException)
			{
				var response = webException.Response as HttpWebResponse;
				if (response != null && response.StatusCode == HttpStatusCode.Conflict)
				{
					throw new TableAlreadyExistsException();
				}
				throw;
			}
			return new RestCloudTable(accountName, accountKey, tableName);
		}

		/// <summary>
		/// Deletes an entity if it exists, nothing otherwise.
		/// </summary>
		/// <param name="partitionKey"></param>
		/// <param name="rowKey"></param>
		/// <param name="eTag"></param>
		public void DeleteEntity(string partitionKey, string rowKey, string eTag)
		{
			if (String.IsNullOrWhiteSpace(eTag)) eTag = "*";
			DeleteEntity(accountName, accountKey, tableName, partitionKey, rowKey, 
				eTag, Timeout.ToString(CultureInfo.InvariantCulture), callback);
		}

		private static void DeleteEntity(string accountName, string accountKey, string tableName,
			string partitionKey, string rowKey, string eTag, string timeout,
			ServerCertificateValidationCallback callback)
		{
			Uri uri =
				new Uri(String.Format("{0}{1}", GetTableUrlText(accountName, tableName),
				                      String.Format("(PartitionKey='{0}',RowKey='{1}'){2}", partitionKey,
				                                    rowKey, string.Format("?timeout={0}", timeout))));
			var request = BuildRequestCore(uri);
			request.KeepAlive = true;
			request.ContentLength = 0;
			request.Method = "DELETE";
			request.Headers.Add("x-ms-version", XMsVersion);
			request.Headers.Add("If-Match", eTag);
			request.UserAgent = UserAgent;
			SignRequest(accountName, accountKey, request);

			RemoteCertificateValidationCallback oldCallback =
				ServicePointManager.ServerCertificateValidationCallback;
			try
			{
				ServicePointManager.ServerCertificateValidationCallback =
					(sender, certificate, chain, errors) => 
						callback(chain, errors, sender, certificate, request, oldCallback);
				request.GetResponse();
			}
			catch (WebException webException)
			{
				var response = webException.Response as HttpWebResponse;
				if (response != null && response.StatusCode == HttpStatusCode.NotFound) return;
				throw;
			}
			finally
			{
				ServicePointManager.ServerCertificateValidationCallback = oldCallback;
			}
		}

		private ServerCertificateValidationCallback
			callback =
				(chain, errors, sender, certificate, request, oldCallback)
				=>
				{
					if (sender == request)
						return OnServerCertificateValidationCallback(sender, certificate, chain, errors);
					return oldCallback(sender, certificate, chain, errors);
				};

		private ServerCertificateValidationCallback ServerCertificateValidationCallback
		{
			get
			{
				return callback;
			}
			set
			{
				callback = value;
			}
		}

		private static XElement RetrieveEntity(string accountName, string accountKey, string tableName,
			string partitionKey, string rowKey, string timeout, ServerCertificateValidationCallback callback)
		{
			Uri uri =
				new Uri(String.Format("{0}{1}", GetTableUrlText(accountName, tableName),
									  String.Format("(PartitionKey='{0}',RowKey='{1}'){2}", partitionKey,
													rowKey, string.Format("?timeout={0}", timeout))));
			var request = BuildRequestCore(uri);
			request.KeepAlive = true;
			request.ContentLength = 0;
			request.Method = WebRequestMethods.Http.Get;
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			SignRequest(accountName, accountKey, request);

			RemoteCertificateValidationCallback oldCallback = ServicePointManager.ServerCertificateValidationCallback;
			try
			{
				ServicePointManager.ServerCertificateValidationCallback =
					(sender, certificate, chain, errors) =>
					callback(chain, errors, sender, certificate, request, oldCallback);

				var response = request.GetResponse();
				var stream = response.GetResponseStream();
				if (stream == null) return null;
				using (var reader = new StreamReader(stream))
				{
					return XElement.Load(reader);
				}
			}
			catch (WebException webException)
			{
				HttpWebResponse response = webException.Response as HttpWebResponse;
				if (response != null && response.StatusCode == HttpStatusCode.NotFound) return null;
				throw;
			}
			finally
			{
				ServicePointManager.ServerCertificateValidationCallback = oldCallback;
			}
		}

		public T RetrieveEntity<T>(string partitionKey, string rowKey) where T : TableEntity, new()
		{
			var doc = RetrieveEntity(accountName, accountKey, tableName, partitionKey, rowKey,
			                         Timeout.ToString(CultureInfo.InvariantCulture), callback);
			return ToEntity<T>(doc);
		}

		public IEnumerable<T> QueryEntities<T>(TableQuery<T> tableQuery) where T : TableEntity, new()
		{
			var doc = QueryEntity(accountName, accountKey, tableName, tableQuery);
			return ToEntities<T>(doc);
		}

		/// <summary>
		/// Replace
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="entity"></param>
		public void UpdateEntity<T>(T entity) where T : TableEntity
		{
			UpdateEntity(entity, WebRequestMethods.Http.Put, Timeout.ToString(CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// Merge
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="entity"></param>
		public void MergeEntity<T>(T entity) where T : TableEntity
		{
			UpdateEntity(entity, "MERGE", Timeout.ToString(CultureInfo.InvariantCulture));
		}

		private void UpdateEntity<T>(T entity, string method, string timeout) where T : TableEntity
		{
			if (entity == null) throw new ArgumentNullException("entity");
			Uri uri =
				new Uri(String.Format("{0}{1}", GetTableUrlText(accountName, tableName),
				                      String.Format("(PartitionKey='{0}',RowKey='{1}'){2}", entity.PartitionKey,
													entity.RowKey, string.Format("?timeout={0}", timeout))));
			var request = BuildRequestCore(uri);
			request.Method = method;
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			request.Headers.Add("If-Match", entity.ETag);
			var stream = GetRequestStreamForEntity(entity, request);

			request.ContentLength = stream.Length;
			SignRequest(accountName, accountKey, request);
			var requestStream = request.GetRequestStream();
			stream.CopyTo(requestStream);
			requestStream.Close();
			try
			{
				request.GetResponse();
			}
			catch (WebException webException)
			{
				var response = webException.Response as HttpWebResponse;
				// not really the place for this method to say what is and isn't correct
				// when updating something that got deleted--just return w/o exception
				if (response != null && response.StatusCode == HttpStatusCode.NotFound) return;
				throw;
			}
		}

		/// <summary>
		/// Inserts an entity
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="entity"></param>
		/// http://msdn.microsoft.com/en-ca/library/windowsazure/dd179433.aspx
		public void InsertEntity<T>(T entity) where T: TableEntity
		{
			if (entity == null) throw new ArgumentNullException("entity");
			Uri uri = new Uri(
				String.Format("{0}{1}", GetTableUrlText(accountName, tableName),
				string.Format("()?timeout={0}", Timeout.ToString(CultureInfo.InvariantCulture))));
			var request = BuildRequestCore(uri);
			request.Method = WebRequestMethods.Http.Post;
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			var stream = GetRequestStreamForEntity(entity, request);
			request.ContentLength = stream.Length;
			SignRequest(accountName, accountKey, request);
			var requestStream = request.GetRequestStream();
			stream.CopyTo(requestStream);
			requestStream.Close();
			try
			{
				request.GetResponse();
			}
			catch (WebException webException)
			{
				var response = webException.Response as HttpWebResponse;
				if (response != null && response.StatusCode == HttpStatusCode.Conflict)
				{
					throw new EntityAlreadyExistsException();
				}
				throw;
			}
		}

		private static XmlDocument QueryEntity<T>(string accountName, string accountKey,
			string tableName, TableQuery<T> tableQuery) where T : ITableEntity, new()
		{
			var tableUri = new Uri(GetTableUrlText(accountName, tableName));
			var uri = BuildQueryUri(tableQuery, tableUri);
			var request = BuildRequestCore(uri);

			request.KeepAlive = true;
			request.ContentLength = 0;
			request.Method = WebRequestMethods.Http.Get;
			request.Headers["x-ms-version"] = XMsVersion;
			request.UserAgent = UserAgent;
			var resource = request.RequestUri.PathAndQuery;
			if (resource.Contains("?"))
			{
				resource = resource.Substring(0, resource.IndexOf("?", StringComparison.Ordinal));
			}
			request.Headers.Add("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));

			string authorizationHeader = CreateLiteAuthorizationHeader(request, resource,
																	   accountName, accountKey);
			request.Headers.Add("Authorization", authorizationHeader);

			var response = request.GetResponse();
			var stream = response.GetResponseStream();
			if (stream == null) return null;
			using (var reader = new StreamReader(stream))
			{
				var doc = new XmlDocument();
				doc.Load(reader);
				return doc;
			}
		}
	}
}