using System;
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
	public class RestCloudTableExistingDataTests
	{
		private const string TableName = "test";
		private static CloudTable table;
		private static string accountName;
		private static string accountKey;

		[TestMethod]
		public void SmokeTest()
		{
			var expected = new ContactEntity("Peter", "Ritchie")
			               {
			               	Email = "1@2.com",
			               	PhoneNumber = "555-0123"
			               };
			var operation = TableOperation.Retrieve<ContactEntity>("Ritchie", "Peter");

			var tableResult = table.Execute(operation);

			Assert.IsNotNull(tableResult, "Azure Table not initialized correctly!");
			var entity = ((ContactEntity)tableResult.Result);
			Assert.IsTrue(
				((Func<ContactEntity, ContactEntity, bool>) ((e, a) => e.FirstName == a.FirstName && e.LastName == a.LastName))(
					expected, entity));
			Assert.IsTrue(((Func<ContactEntity, ContactEntity, bool>) ((_expected, _actual) =>
			                                                           _expected.LastName == _actual.LastName && _expected.FirstName == _actual.FirstName &&
			                                                           _expected.Email == _actual.ETag && _expected.PhoneNumber == _actual.PhoneNumber))(expected, entity));
			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("555-0123", entity.PhoneNumber);
		}

		[ClassInitialize]
		public static void Initialize(TestContext context)
		{
			ConnectionStringSettingsCollection settings =
				ConfigurationManager.ConnectionStrings;
			ConnectionStringSettings connectionStringSettings = settings["Azure"];
			var builder = new DbConnectionStringBuilder
			              {
			              	ConnectionString = connectionStringSettings.ConnectionString
			              };
			accountName = (string) builder["AccountName"];
			accountKey = (string) builder["AccountKey"];
			var account = CloudStorageAccount.Parse(connectionStringSettings.ConnectionString);
			CloudTableClient tableClient = account.CreateCloudTableClient();

			// Create Table
			table = tableClient.GetTableReference(TableName);
			var b = table.DeleteIfExists();
			Console.WriteLine(string.Format("deleted table {0}: {1}", TableName, b));
			table.CreateIfNotExists();

			// Insert Entity
			var person = new ContactEntity("Peter", "Ritchie") { Email = "1@2.com", PhoneNumber = "555-0123" };
			table.Execute(TableOperation.Insert(person));
		}

		[ClassCleanup]
		public static void Cleanup()
		{
			table.DeleteIfExists();
			table = null;
		}

		RestCloudTable restTable;

		[TestInitialize]
		public void Setup()
		{
			restTable = new RestCloudTable(accountName,
			                               accountKey,
			                               TableName);
		}

		[TestMethod]
		public void GetEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");

			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("555-0123", entity.PhoneNumber);
		}

		[TestMethod]
		public void QueryEntityTest()
		{
			TableQuery<ContactEntity> tableQuery =
				new TableQuery<ContactEntity>().Where(
					TableQuery.CombineFilters(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "Ritchie"),
					                          TableOperators.And,
					                          TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, "Peter")));

			var entities = restTable.QueryEntities(tableQuery);
			Assert.IsNotNull(entities);
			Assert.AreEqual(1, entities.Count());
			var entity = entities.ElementAt(0);

			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("555-0123", entity.PhoneNumber);
		}

		[TestMethod]
		public void DeleteExistingEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			entity.ETag = entity.ETag ?? "*";

			restTable.DeleteEntity("Ritchie", "Peter", entity.ETag);
			entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			Assert.IsNull(entity);
		}

		[TestMethod]
		public void DeleteNonxistantEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			entity.ETag = entity.ETag ?? "*";

			restTable.DeleteEntity("Ritchie", "Peter", entity.ETag);
			restTable.DeleteEntity("Ritchie", "Peter", entity.ETag);
			entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			Assert.IsNull(entity);
		}

		[TestMethod]
		public void UpdateEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			entity.ETag = entity.ETag ?? "*";
			entity.PhoneNumber = "613-231-1165";

			//table.Execute(TableOperation.Replace(entity));
			restTable.UpdateEntity(entity);
			entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");

			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("613-231-1165", entity.PhoneNumber);
		}

		[TestMethod]
		public void UpdateNonexistantEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			entity.ETag = entity.ETag ?? "*";
			entity.PhoneNumber = "613-231-1165";

			restTable.DeleteEntity(entity.PartitionKey, entity.RowKey, entity.ETag);
			restTable.UpdateEntity(entity);
			entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");

			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("613-231-1165", entity.PhoneNumber);
		}

		[TestMethod]
		public void InsertEntityTest()
		{
			const string firstName = "first";
			const string lastName = "last";
			const string email = "2@1.com";
			const string phoneNumber = "613-555-1234";

			restTable.InsertEntity(new ContactEntity(firstName, lastName)
			                       {
			                       	Email=email,
			                       	PhoneNumber = phoneNumber
			                       });
			var entity = restTable.RetrieveEntity<ContactEntity>(lastName, firstName);

			Assert.AreEqual(lastName, entity.LastName);
			Assert.AreEqual(firstName, entity.FirstName);
			Assert.AreEqual(email, entity.Email);
			Assert.AreEqual(phoneNumber, entity.PhoneNumber);
		}

		[TestMethod, ExpectedException(typeof(EntityAlreadyExistsException))]
		public void InsertExistingEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");

			restTable.InsertEntity(entity);
		}

		[TestMethod]
		public void MergeEntityTest()
		{
			var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
			entity.ETag = entity.ETag ?? "*";
			entity.PhoneNumber = "613-231-1165";

			restTable.MergeEntity(entity);
			entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");

			Assert.AreEqual("Ritchie", entity.LastName);
			Assert.AreEqual("Peter", entity.FirstName);
			Assert.AreEqual("1@2.com", entity.Email);
			Assert.AreEqual("613-231-1165", entity.PhoneNumber);
		}

		//[TestMethod]
		//public void Test2()
		//{
		//    try
		//    {
		//        XElement doc = restTable.GetEntity("Ritchie", "Peter");
		//        var properties = from property in doc.Descendants()
		//                       where property.Name.Namespace == "http://schemas.microsoft.com/ado/2007/08/dataservices"
		//                       select property;
		//        var entity = new ContactEntity();
		//        entity.PartitionKey = properties.First(e => e.Name.LocalName == "PartitionKey").Value;
		//        entity.RowKey = properties.First(e => e.Name.LocalName == "RowKey").Value;
		//        var type = entity.GetType();
		//        foreach (var prop in type.GetProperties(BindingFlags.SetField | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
		//        {
		//            var xElement = properties.FirstOrDefault(value => 0 == String.Compare(value.Name.LocalName, prop.Name, StringComparison.OrdinalIgnoreCase));
		//            if(null != xElement)
		//            {
		//                var c = TypeDescriptor.GetConverter(prop.PropertyType);
		//                if(c != null && c.CanConvertFrom(typeof(String)) && c.CanConvertTo(prop.PropertyType))
		//                {
		//                    prop.SetValue(entity, c.ConvertFromInvariantString(xElement.Value), null);
		//                }
		//            }
		//        }
		//        Console.WriteLine('x');
		//        // Create Table
		//        // Query Entities
		//        // Insert Entity

		//    }
		//    catch (Exception ex)
		//    {
		//        Trace.WriteLine(ex);
		//        throw;
		//    }
		//}
	}
}