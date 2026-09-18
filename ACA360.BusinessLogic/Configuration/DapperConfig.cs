using Dapper;
using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace ACA360.BusinessLogic.Configuration
{
    public static class DapperConfig
    {
        public static void ConfigureColumnMapping()
        {
            // 1. Find all classes inside your Models namespace
            // (Make sure this matches the actual namespace of your models)
            var modelTypes = Assembly.GetAssembly(typeof(Core.Models.EmployeeStatus))
                ?.GetTypes()
                .Where(t => t.IsClass && t.Namespace != null && t.Namespace.StartsWith("ACA360.Core.Models"));

            if (modelTypes == null) return;

            // 2. Loop through every model and teach Dapper how to read it
            foreach (var type in modelTypes)
            {
                SqlMapper.SetTypeMap(type, new CustomPropertyTypeMap(type, (t, sqlColumnName) =>
                {
                    // A. Check if any property has a [Column("sqlColumnName")] tag
                    var mappedProperty = t.GetProperties().FirstOrDefault(prop =>
                        prop.GetCustomAttributes(false)
                            .OfType<ColumnAttribute>()
                            .Any(attr => attr.Name.Equals(sqlColumnName, StringComparison.OrdinalIgnoreCase))
                    );

                    // B. If no tag is found, fallback to standard Dapper name matching
                    return mappedProperty ?? t.GetProperties().FirstOrDefault(p =>
                        p.Name.Equals(sqlColumnName, StringComparison.OrdinalIgnoreCase)
                    );
                }));
            }
        }
    }
}