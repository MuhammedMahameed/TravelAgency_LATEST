using Stripe;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHostedService<TravelAgency.Helpers.ReminderService>();
builder.Services.AddSession();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Stripe configuration (test keys for localhost)
var stripeSecret = builder.Configuration["Stripe:SecretKey"];
if (!string.IsNullOrWhiteSpace(stripeSecret) && !stripeSecret.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
{
    StripeConfiguration.ApiKey = stripeSecret;
}

var app = builder.Build();
app.UseSession();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();