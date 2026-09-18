using ACA360.API.Middleware;
using ACA360.Logging.Interfaces;
using ACA360.Repositories.Interfaces;
using ACA360.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using ACA360.Repositories.Implementations;
using ACA360.BusinessLogic.Interfaces;
using ACA360.BusinessLogic.Services;
// Add these usings
var builder = WebApplication.CreateBuilder(args);

// 1. Add Services (Same DB Connection as Web)
string connString = builder.Configuration.GetConnectionString("DefaultConnection");

// Register your existing services (Reuse form Core/Services)
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IApiClientService, ApiClientService>();
// Employee service: needs connection string + logger.
builder.Services.AddScoped<IEmployeeService>(provider =>
    new EmployeeService(connString, provider.GetRequiredService<ILoggerService>(),provider.GetRequiredService<IDataAuditlogService>(),provider.GetRequiredService<IFlagRepository>(), provider.GetRequiredService<IACALogicService>()));
// Register Dapper/Repo if needed
builder.Services.AddScoped<IDbConnection>(sp => new SqlConnection(connString));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(); // Auto-generates documentation

var app = builder.Build();

// 2. Configure Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 3. REGISTER GLOBAL KILL SWITCH
app.UseMiddleware<ApiStatusMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();