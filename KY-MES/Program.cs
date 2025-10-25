using KY_MES.Application.Helpers;
using KY_MES.Controllers;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Infra;
using KY_MES.Infra.CrossCutting;
using KY_MES.Infra.CrossCutting.Data;
using KY_MES.Services;
using KY_MES.Services.DomainServices.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));



builder.Services.AddCors(options => {
    options.AddPolicy("AllowAllOrigins",
        builder =>
        {
            builder.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader(); 
        });
});
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IMESService, MESService>();
builder.Services.AddScoped<IKY_MESApplication, KY_MESApplication>();
builder.Services.AddScoped<ISpiRepository, SpiRepository>();
builder.Services.AddSingleton<SPIHelpers>();

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
