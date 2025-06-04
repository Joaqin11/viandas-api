using AccuViandas.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer; // NEW
using Microsoft.IdentityModel.Tokens;             // NEW
using System.Text;                                // NEW
using Microsoft.OpenApi.Models;                   // NEW - Para configuraci�n de Swagger JWT
using System.IdentityModel.Tokens.Jwt;
using AccuViandas.Services; // NEW
using Microsoft.Extensions.Logging; // NEW: Para usar ILogger

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//builder.Services.AddControllers();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Para que el JSON sea insensible a may�sculas/min�sculas en los nombres de propiedades
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- AGREGAR ESTO para configurar Entity Framework Core con SQLite ---
// Aseg�rate de tener 'using Microsoft.EntityFrameworkCore;' al inicio del archivo si no est�
builder.Services.AddDbContext<MenuDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
// --- FIN DE LA ADICI�N ---

// --- AGREGAR ESTO para configurar Entity Framework Core con SQLite ---
// Esto es para guardar datos historicos
builder.Services.AddDbContext<ArchiveDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ArchiveConnection")));
// --- FIN DE LA ADICI�N ---

// --- NEW: Configuraci�n de JWT Authentication ---

// Define la clave secreta para firmar los tokens. �Debe ser robusta y segura!
var jwtSecretKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtSecretKey))
{
    // En un entorno de producci�n, esto deber�a ser un error fatal o una configuraci�n segura.
    // Para desarrollo, podemos usar una clave por defecto si no est� en appsettings.json.
    jwtSecretKey = "SUPERSECRETO_CLAVE_JWT_MUY_LARGA_Y_COMPLEJA_PARA_PRODUCCION_CAMBIALA_YA_1234567890";
    builder.Configuration["Jwt:Key"] = jwtSecretKey; // Establecerla en la configuraci�n para que funcione.
    Console.WriteLine("Advertencia: No se encontr� Jwt:Key en la configuraci�n. Usando clave por defecto para desarrollo.");
}

var key = Encoding.ASCII.GetBytes(jwtSecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Solo para desarrollo HTTP. En producci�n, siempre true.
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, // Validar la clave de firma
        IssuerSigningKey = new SymmetricSecurityKey(key), // La clave que usamos
        ValidateIssuer = false, // No validamos el emisor (por simplicidad, en prod. se valida)
        ValidateAudience = false, // No validamos la audiencia (por simplicidad, en prod. se valida)
        ClockSkew = TimeSpan.Zero // El token expira exactamente a la hora definida, sin margen
    };
});

// --- END NEW: Configuraci�n de JWT Authentication ---

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// --- NEW: Configuraci�n de Swagger para que admita JWT ---
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AccuViandas API", Version = "v1" });

    // Definir el esquema de seguridad para JWT
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Ingrese 'Bearer' [espacio] y luego su token en el campo de texto a continuaci�n. Ejemplo: 'Bearer eyJhbGciOiJIUzI1Ni...',",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Requerir el esquema de seguridad para todas las operaciones (o espec�ficas)
    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});
// --- END NEW: Configuraci�n de Swagger para que admita JWT ---

// --- NEW: Registrar Email Service ---
builder.Services.AddTransient<IEmailService, EmailService>(); // Registra el servicio de email
// --- END NEW ---

// --- NEW: Register the Background Service ---
builder.Services.AddHostedService<MenuEmailBackgroundService>();
// --- END NEW ---
// --- NEW: Register the Background Service ---
builder.Services.AddHostedService<DataMaintenanceBackgroundService>(); // NUEVO: Servicio de mantenimiento de datos
// --- END NEW ---

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// --- NEW: Habilitar Autenticaci�n y Autorizaci�n en el pipeline ---
app.UseAuthentication(); // Primero autentica al usuario
app.UseAuthorization();  // Luego autoriza al usuario (basado en roles, pol�ticas, etc.)
// --- END NEW ---

app.UseAuthorization();

app.MapControllers();

app.Run();
