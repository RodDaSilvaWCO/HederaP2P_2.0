namespace UnoSysKernel
{
    using System;
    using System.Threading.Tasks;
    using UnoSys.Api;
    using UnoSys.Api.Exceptions;
    using UnoSys.Api.Models;
    using UnoSysCore;

    internal partial class ApiManager : SecuredKernelService, IApiManager
    {
        public async Task DatabaseCreateTableAsync(string userSessionToken, string databaseSessionToken, string tableName, string tableDescription, TableSchema tableSchema)
        {
            ThrowIfParameterNullOrEmpty("UserSessionToken", userSessionToken);
            ThrowIfParameterNullOrEmpty("DatabaseSessionToken", databaseSessionToken);
            ThrowIfParameterIsNotLegalIdentifier("TableName", tableName);
            if (tableSchema == null!)
            {
                throw new UnoSysArgumentException("Parameter 'TableSchema' must not be null.");
            }
            foreach( var fd in tableSchema.Fields)
            {
                ThrowIfParameterNullOrEmpty("TableSchema Field", fd.Name);
                if( fd.TypeDefinition == null)
                {
                    throw new UnoSysArgumentException($"Missing TableSchema field definition.");
                }
                if( fd.TypeDefinition.Type == TableFieldType.UNDEFINED)
                {
                    throw new UnoSysArgumentException($"Invalid TableSchema field type.");
                }
                if ( ! Utilities.IsValidCSharpIdentifier(fd.Name)) 
                {
                    throw new UnoSysArgumentException($"Invalid TableSchema field name {fd.Name}.");
                }
            }
            var ust = new UserSessionToken(userSessionToken);
            var rst = new DatabaseSessionToken(databaseSessionToken);
            if (!wcContext.CheckResourceOwnerContext(ust, rst))
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            await securityContext.DatabaseCreateTableAsync( ust, rst,  tableName, tableDescription, tableSchema).ConfigureAwait(false);
        }

        public void DatabaseCreateTable(string userSessionToken, string databaseSessionToken, string tableName, string tableDescription, TableSchema schema)
        {
            DatabaseCreateTableAsync(userSessionToken, databaseSessionToken, tableName, tableDescription, schema).Wait();
        }
    }
}