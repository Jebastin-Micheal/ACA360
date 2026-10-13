using ACA360.Core.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ACA360.Repositories.Implementations
{
    public partial class DataProcessingRepository
    {
        // One read per entity. No repeated OFFSET calls or buffered list of all rows.
        public async IAsyncEnumerable<IDictionary<string, object?>> ReadImportAnalysisExportRowsAsync(
            int fileLogId, string entity, string importMode,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var sheet = ImportAnalysisCatalog.Find(entity);
            if (fileLogId <= 0 || sheet == null || !ImportAnalysisCatalog.IsMode(importMode))
                throw new ArgumentException("Invalid analysis export request.");

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.sp_ImportAnalysis_ExportRows", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 180
            };
            command.Parameters.Add("@FileLogId", SqlDbType.Int).Value = fileLogId;
            command.Parameters.Add("@Entity", SqlDbType.VarChar, 20).Value = sheet.Entity;
            command.Parameters.Add("@ImportMode", SqlDbType.VarChar, 20).Value = importMode;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var names = new string[reader.FieldCount];
            for (int i = 0; i < names.Length; i++) names[i] = reader.GetName(i);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Length; i++)
                    row[names[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                yield return row;
            }
        }
    }
}
