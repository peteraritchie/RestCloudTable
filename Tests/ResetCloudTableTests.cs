using System.Configuration;
using System.Data.Common;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using PRI;

namespace Tests
{
	[TestClass]
	public class ResetCloudTableTests
	{
		private static string accountName;
		private static string accountKey;
		private static string connectionString;

		[ClassInitialize]
		public static void Initialize(TestContext context)
		{
			ConnectionStringSettingsCollection settings =
				ConfigurationManager.ConnectionStrings;
			ConnectionStringSettings connectionStringSettings = settings["Azure"];
			connectionString = connectionStringSettings.ConnectionString;
			var builder = new DbConnectionStringBuilder
			{
				ConnectionString = connectionString
			};
			accountName = (string)builder["AccountName"];
			accountKey = (string)builder["AccountKey"];
		}

		[TestMethod]
		public void TestCreate()
		{
			var table = RestCloudTable.Create(accountName, accountKey, "todelete");
			Assert.IsNotNull(table);
			var account = CloudStorageAccount.Parse(connectionString);
			CloudTableClient tableClient = account.CreateCloudTableClient();
			Assert.IsTrue(tableClient.ListTables().Any(e => e.Name == "todelete"));
			var cloudTable = tableClient.GetTableReference("todelete");

			cloudTable.DeleteIfExists();
		}

		[TestMethod]
		public void TestDelete()
		{
			// Create Table
			var account = CloudStorageAccount.Parse(connectionString);
			CloudTableClient tableClient = account.CreateCloudTableClient();

			var table = tableClient.GetTableReference("creating");
			table.CreateIfNotExists();

			RestCloudTable.Delete(accountName, accountKey, "creating");
			Assert.IsFalse(tableClient.ListTables().Any(e => e.Name == "creating"));
		}
	}
}
