using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using KY_MES.Services;
using KY_MES.Domain.V1.DTOs.OutputModels;
using KY_MES.Domain.V1.DTOs.InputModels;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class TokenLoggerWorker : BackgroundService
{
    private readonly ILogger<TokenLoggerWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;
    private string _lastToken = null;

    public TokenLoggerWorker(ILogger<TokenLoggerWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mesService = new MESService();

        var signInRequest = new SignInRequestModel
        {
            Username = Environment.GetEnvironmentVariable("Username"),
            Password = Environment.GetEnvironmentVariable("Password")
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var signInResponse = await mesService.SignInAsync(signInRequest);
                var token = signInResponse?.UserToken;

                if (!string.IsNullOrEmpty(token) && token != _lastToken)
                {
                    await SaveUserTokenToDbAsync(token);
                    _lastToken = token;
                    _logger.LogInformation("Token atualizado e salvo no banco.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao renovar/salvar token.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task SaveUserTokenToDbAsync(string userToken)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var cmd = new SqlCommand(@"
            IF EXISTS (SELECT 1 FROM MesUserTokenLog)
                UPDATE MesUserTokenLog SET UserToken = @token, LastUpdated = GETDATE()
            ELSE
                INSERT INTO MesUserTokenLog (UserToken, LastUpdated) VALUES (@token, GETDATE())
        ", connection);

        cmd.Parameters.AddWithValue("@token", userToken);
        await cmd.ExecuteNonQueryAsync();
    }
}