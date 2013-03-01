RestCloudTable
==============

This sample project makes use of the Azure Table Storage REST API.  It's intended to serve as an example of how you might access Azure Table storage with a specific server certificate validation policy.

Specifying server certificate validation policy
-----------------------------------------------

For the most part, operations occur with an instance of RestCloudTable.  To override the server certificate validation, it's simply a matter of setting the `ServerCertificateValidationCallback` property with the delegate you want invoked.

For example, if you want to allow certs that have expired in the last two weeks, you could set the `ServerCertificateValidationCallback` to the following lambda statement:

```C#
	restTable.ServerCertificateValidationCallback =
		(chain, errors, sender, certificate, request, oldCallback) =>
		{
			if (errors == SslPolicyErrors.None) return true;
			if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
			{
				var effectiveDate =
					DateTime.Parse(certificate.GetEffectiveDateString());
				var expirationDate =
					DateTime.Parse(certificate.GetExpirationDateString());
				if (effectiveDate < DateTime.Now || expirationDate.AddDays(14) > DateTime.Now)
				{
					return true;
				}
			}
			return false;
		};
```

With operations that occur through static methods on `RestCloudTable`, and optional delegate can be passed in.  For example:
```C#
	var table = RestCloudTable
		.Create(accountName, accountKey, "myTable",
				(chain, errors, sender, certificate, request, oldCallback) =>
				{
				    if (errors == SslPolicyErrors.None) return true;
				    if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
				    {
				        var effectiveDate =
				        	DateTime.Parse(certificate.GetEffectiveDateString());
				        var expirationDate =
				        	DateTime.Parse(certificate.GetExpirationDateString());
				        if (effectiveDate < DateTime.Now || expirationDate.AddDays(14) > DateTime.Now)
				        {
				        	return true;
				        }
				    }
				    return false;
				});

```

The `sender` argument is passed in by the networking subsystem, and may or may not be the `HttpWebRequest` associated with the request.  Witin the `RestCloudTable` code, it passes in the actual `HttpWebRequest` that it used so that you can compare with the `sender` argument to see if this certificate validation applies to the current `RestCloudTable` request.  Also, each time a reqest is made this callback is set in the network subsystem to reduce the risk (but not illiminate) race conditions of other threads replacing this "global" value.  `oldCallback` is the value of the callback that was in place when the request was started.  If you decide it's useful to chain through to any existing callbacks, you can simply invoke this delegate.


The API
-------
The API is similar, but different, to the .NET API.  There's a single class `RestCloudTable` that contains all the table operations that can be performed.

Those operations are:
Create table
Delete table
Insert entity
Delete entity
Update (Replace) entity
Merge entity
Query entities
Retrieve entity

In general, this API re-uses some of the .NET API.  Some things that don't access Azure directly are re-used; like `TableEntity`, `TableQuery`, and `UriQueryBuilder`.

For the most part, working with this API is the same as working with the .NET API, everything works with `TableEntity`s.  You create entities classes that you want to store in Azure Table storage and derive them from `TableEntity` and things like `PartitionKey` and `RowKey` come for free.

### Create table
```C#
var table = RestCloudTable.Create(accountName, accountKey, "myTableName");
```
If the table already exists a `TableExistsException` will be thrown.

### Delete table
```C#
RestCloudTable.Delete(accountName, accountKey, "myTableName");
```
If the table already exists a `TableExistsException` will be thrown.

### Insert entity
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);
	restTable.InsertEntity(new ContactEntity("firstName", "lastName")
			                {
			                Email="email",
			                PhoneNumber = "phoneNumber"
			                });
```
If the entity already exists, `EntityAlreadyExistsException` will be thrown.

### Delete entity
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);
	var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
	entity.ETag = entity.ETag ?? "*";

	restTable.DeleteEntity("Ritchie", "Peter", entity.ETag);
```

In the same way as the .NET API requires an `ETag`, so does the REST API.  Use `"*"` as an `ETag` to avoid concurrency conflicts.  See the Azure Table Storage documentation for more information on `ETag`

### Update (Replace) entity
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);

	var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
	entity.ETag = entity.ETag ?? "*";
	entity.PhoneNumber = "613-231-1165";

	restTable.UpdateEntity(entity);
```
If the entity didn't exist the method simply returns with no exception.

### Merge entity
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);
	var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
	entity.ETag = entity.ETag ?? "*";
	entity.PhoneNumber = "613-231-1165";

	restTable.MergeEntity(entity);
```
If the entity didn't exist the method simply returns with no exception.

### Query entities
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);
	TableQuery<ContactEntity> tableQuery =
		new TableQuery<ContactEntity>().Where(
			TableQuery.CombineFilters(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "Ritchie"),
					                    TableOperators.And,
					                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, "Peter")));

	IEnumerable<ContactEntity> entities = restTable.QueryEntities(tableQuery);
```

### Retrieve entity
```C#
	restTable = new RestCloudTable(accountName,
			                        accountKey,
			                        TableName);
	var entity = restTable.RetrieveEntity<ContactEntity>("Ritchie", "Peter");
```

If the entity does not exist, `null` is returned.


For more detail on API usage, see the tests.

Running the tests
-----------------
As you might imagine, I did not include my account name and account key in the project commited in GitHub.  In order to run the tests you need to create two files `privateAppSettings.config` and `privateConnectionStrings.config`.  These two files contain the private settings used by the tests to connect to your Azure Table storage instance.

### Example privateAppSettings.config
```xml
<?xml version="1.0" encoding="utf-8" ?>
<appSettings>
	<add key="tableName" value="testTableName" />
	<add key="accountName" value="myAccountName" />
</appSettings>
```
### Eample privateConnectionStrings.config
```xml
<connectionStrings>
	<add name="Azure"
	 connectionString="DefaultEndpointsProtocol=https;AccountName=myAccountName;AccountKey=myAccountKey" />
</connectionStrings>
```

Until those config files are created on your local computer the tests will not run.

The tests assume you're hitting a production Azure server, it doesn't use a local emulated storage service.