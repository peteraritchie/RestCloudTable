using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Data.OData;

namespace PRI
{
	internal class ODataRequestMessageAdapter : IODataRequestMessage, IDisposable
	{
		private readonly HttpWebRequest request;
		private MemoryStream stream;

		public ODataRequestMessageAdapter(HttpWebRequest request)
		{
			this.request = request;
			stream = new MemoryStream();
		}

		public IEnumerable<KeyValuePair<string, string>> Headers
		{
			get
			{
				return (from string text in request.Headers.Keys
				        select new KeyValuePair<string, string>(text, request.Headers[text]))
					.ToList();
			}
		}
		public string Method
		{
			get
			{
				return request.Method;
			}
			set
			{
				throw new NotSupportedException();
			}
		}
		public Uri Url
		{
			get
			{
				return request.RequestUri;
			}
			set
			{
				throw new NotSupportedException();
			}
		}
		public string GetHeader(string headerName)
		{
			return headerName == "Content-Type" ? request.ContentType : request.Headers.Get(headerName);
		}

		public Stream GetStream()
		{
			stream.Seek(0, SeekOrigin.Begin);
			return stream;
		}
		public void SetHeader(string headerName, string headerValue)
		{
			if (headerName == "Content-Type")
			{
				request.ContentType = headerValue;
				return;
			}
			request.Headers.Add(headerName, headerValue);
		}

		public void Dispose()
		{
			using (stream)
			{
				stream = null;
			}
		}
	}
}