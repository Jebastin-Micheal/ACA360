using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.Common;
namespace ACA360.Data.Helpers
{
    public class DbHelper
    {
        private readonly IConfiguration _configuration;

        public DbHelper(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        private string GetConnectionString(string name)
        {
            return _configuration.GetConnectionString(name)
                ?? throw new InvalidOperationException($"Connection string '{name}' is missing.");
        }

        #region Async Methods

        public async Task<T?> ExecuteScalarAsync<T>(
             string storedProcedure,
             Action<SqlParameterCollection>? paramBuilder = null,
             string connectionName = "DefaultConnection") // optional
        {
            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return result == null || result == DBNull.Value ? default : (T)result;
        }

        public async Task<int> ExecuteNonQueryAsync(
              string storedProcedure,
              Action<SqlParameterCollection>? paramBuilder = null,
              string connectionName = "DefaultConnection")
        {
            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<List<T>> ExecuteReaderAsync<T>(
              string storedProcedure,
              Func<SqlDataReader, T> mapper,
              Action<SqlParameterCollection>? paramBuilder = null,
              string connectionName = "DefaultConnection")
        {
            var results = new List<T>();

            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                try
                {
                    results.Add(mapper(reader));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Mapping error in stored procedure '{storedProcedure}'", ex);
                }
            }

            return results;
        }


        public async Task<List<T>> ExecuteReaderAsync<T>(
               string storedProcedure,
               Func<SqlDataReader, Task<T>> asyncMapper,
               Action<SqlParameterCollection>? paramBuilder = null,
               string connectionName = "DefaultConnection")
        {
            var results = new List<T>();

            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                try
                {
                    results.Add(await asyncMapper(reader).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Async mapping error in stored procedure '{storedProcedure}'", ex);
                }
            }

            return results;
        }

        public async Task<T?> ExecuteSingleAsync<T>(
              string storedProcedure,
              Func<SqlDataReader, T> mapper,
              Action<SqlParameterCollection>? paramBuilder = null,
              string connectionName = "DefaultConnection")
        {
            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return mapper(reader);
            }

            return default;
        }

        public async Task ExecuteMultiReaderAsync(
              string storedProcedure,
              Action<SqlParameterCollection>? paramBuilder,
              Func<SqlDataReader, Task> handleReaders,
              string connectionName = "DefaultConnection")
        {
            await using var connection = new SqlConnection(GetConnectionString(connectionName));
            await using var command = new SqlCommand(storedProcedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };

            paramBuilder?.Invoke(command.Parameters);

            await connection.OpenAsync().ConfigureAwait(false);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            await handleReaders(reader).ConfigureAwait(false);
        }


        //ganesh
        public async Task<DataTable> GetDataTableByProcAsync(string procName, Action<SqlParameterCollection>? paramFn, string connectionName = "DefaultConnection")
        {
            using var conn = new SqlConnection(GetConnectionString(connectionName));
            using var cmd = new SqlCommand(procName, conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            if (paramFn != null)
                paramFn(cmd.Parameters);

            using var da = new SqlDataAdapter(cmd);
            var dt = new DataTable();
            await conn.OpenAsync();
            da.Fill(dt);
            return dt;
        }

        //jebastin
        public async Task<List<T>> ExecuteReaderParametersAsync<T>(string storedProcedure, Func<SqlDataReader, T> mapper, List<SqlParameter> parameters = null, string connectionName = "DefaultConnection")
        {
            var results = new List<T>();

            using (var connection = new SqlConnection(GetConnectionString(connectionName)))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(storedProcedure, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters.ToArray());
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(mapper(reader));
                        }
                    }
                }
            }

            return results;
        }
        #endregion

        #region Utility Helpers

        public static T? GetNullable<T>(IDataReader reader, string column) where T : struct =>
            reader[column] != DBNull.Value ? (T?)reader[column] : null;

        public static string? GetStringSafe(IDataReader reader, string column) =>
            reader[column] != DBNull.Value ? reader[column].ToString() : null;

        #endregion
    }
}
