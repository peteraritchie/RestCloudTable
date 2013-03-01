RestCloudTable
==============

This sample project makes use of the Azure Table Storage REST API

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