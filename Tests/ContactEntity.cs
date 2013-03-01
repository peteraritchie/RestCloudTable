using Microsoft.WindowsAzure.Storage.Table;

namespace Tests
{
	public class ContactEntity : TableEntity
	{
		public ContactEntity(string firstName, string lastName) : base(lastName, firstName)
		{
		}

		public ContactEntity()
		{
		}

		public string FirstName { get { return RowKey; } }
		public string LastName { get { return PartitionKey; } }
		public string Email { get; set; }
		public string PhoneNumber { get; set; }
	}
}