using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

const string baseUrl = "http://localhost:5129";
const string testEmail = "t@gmail.com";
const string testPassword = "A666Az!";

var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var http = new HttpClient( handler );
http.BaseAddress = new Uri( baseUrl );

string? token = null;
var linkId = 0;
string? shortUrl = null;
var categoryId = 0;

var passed = 0;
var failed = 0;

async Task<(HttpStatusCode status, JsonElement? body)> Req(
    HttpMethod method,
    string url,
    bool auth = true,
    object? json = null )
{
    using var req = new HttpRequestMessage( method, url );

    if ( json is not null )
        req.Content = JsonContent.Create( json );

    if ( auth && token is not null )
        req.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", token );

    HttpResponseMessage res;
    try
    {
        res = await http.SendAsync( req );
    }
    catch ( HttpRequestException ex ) when ( ex.InnerException is SocketException )
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine( $"\n  ✗ Cannot connect to {baseUrl}" );
        Console.ResetColor();
        Console.WriteLine( "    Make sure the server is running.\n" );
        Environment.Exit( 1 );
        throw;
    }

    var text = await res.Content.ReadAsStringAsync();

    JsonElement? body = null;
    if ( string.IsNullOrWhiteSpace( text ) ) 
        return (res.StatusCode, body);
    
    try
    {
        body = JsonDocument.Parse( text ).RootElement.Clone();
    }
    catch
    {
        // not json
    }

    return (res.StatusCode, body);
}

// shortcuts
Task<(HttpStatusCode, JsonElement?)> GET( string url, bool auth = true )
{
    return Req( HttpMethod.Get, url, auth );
}

Task<(HttpStatusCode, JsonElement?)> POST( string url, object? body = null, bool auth = true )
{
    return Req( HttpMethod.Post, url, auth, body );
}

Task<(HttpStatusCode, JsonElement?)> PUT( string url, bool auth = true )
{
    return Req( HttpMethod.Put, url, auth );
}

Task<(HttpStatusCode, JsonElement?)> DELETE( string url, bool auth = true )
{
    return Req( HttpMethod.Delete, url, auth );
}

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine( $"\nKiirLink API Test - {baseUrl}" );
Console.ResetColor();

{
    const int timeoutSec = 30;
    var deadline = DateTime.UtcNow.AddSeconds( timeoutSec );
    var ready = false;

    Console.Write( "  Waiting for server " );
    while ( DateTime.UtcNow < deadline )
    {
        try
        {
            using var probe = new HttpClient();
            probe.Timeout = TimeSpan.FromSeconds( 2 );
            
            var r = await probe.GetAsync( $"{baseUrl}/scalar" );

            ready = true;
            break;
        }
        catch
        {
            // still starting
        }

        Console.Write( "." );
        await Task.Delay( 1000 );
    }

    if ( !ready )
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine( $"  Server did not become ready within {timeoutSec}s." );
        Console.WriteLine( "  Start it with:  dotnet run --launch-profile http" );
        Console.ResetColor();
        Environment.Exit( 1 );
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine( " ready!" );
    Console.ResetColor();
}

Section( "Authentication" );

var (regStatus, _) = await POST( "/api/auth/register", new { email = testEmail, password = testPassword }, false );
if ( regStatus == HttpStatusCode.OK )
    Pass( "POST /auth/register  (new user)" );
else if ( regStatus == HttpStatusCode.BadRequest )
    Pass( "POST /auth/register  (user already exists — OK)" );
else
    Fail( "POST /auth/register", $"unexpected {(int)regStatus}" );

var (loginStatus, loginBody) = await POST(
    "/api/auth/login?useCookies=false&useSessionCookies=false",
    new { email = testEmail, password = testPassword },
    false );

if ( loginStatus == HttpStatusCode.OK && loginBody.HasValue )
{
    token = loginBody.Value.GetProperty( "accessToken" ).GetString();
    Pass( "POST /auth/login  (token obtained)" );
    Info( $"token: {token?[..20]}…" );
}
else
{
    Fail( "POST /auth/login", $"{(int)loginStatus}" );
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine( "\nCannot continue without a token. Is the server running?" );
    Console.ResetColor();
    Environment.Exit( 1 );
}

var (unauthStatus, _) = await GET( "/api/links/get", false );
if ( unauthStatus is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect )
    Pass( "GET /api/links/get  without token → 401" );
else
    Fail( "GET /api/links/get  without token", $"expected 401, got {(int)unauthStatus}" );

Section( "Links — shorten & list" );

var (shortenStatus, shortenBody) = await POST( "/api/links/shorten?longUrl=https://www.google.com/" );

if ( shortenStatus == HttpStatusCode.Created && shortenBody.HasValue )
{
    shortUrl = shortenBody.Value.GetProperty( "shortUrl" ).GetString();
    Pass( $"POST /api/links/shorten  →  {shortUrl}" );
}
else
{
    Fail( "POST /api/links/shorten", $"{(int)shortenStatus}" );
}

var (getStatus, getBody) = await GET( "/api/links/get" );
if ( getStatus == HttpStatusCode.OK && getBody.HasValue && getBody.Value.ValueKind == JsonValueKind.Array )
{
    var count = getBody.Value.GetArrayLength();

    foreach ( var item in getBody.Value.EnumerateArray() )
        if ( item.TryGetProperty( "shortUrl", out var su ) && su.GetString() == shortUrl )
        {
            linkId = item.GetProperty( "id" ).GetInt32();
            break;
        }

    Pass( $"GET /api/links/get  →  {count} link(s), our id = {linkId}" );
}
else
{
    Fail( "GET /api/links/get", $"{(int)getStatus}" );
}

Section( "Links — stats" );

if ( linkId > 0 )
{
    var (statsStatus, statsBody) = await GET( $"/api/links/{linkId}/stats" );
    if ( statsStatus == HttpStatusCode.OK && statsBody.HasValue )
    {
        var clicks = statsBody.Value.GetProperty( "totalClicks" ).GetInt32();
        Pass( $"GET /api/links/{linkId}/stats  →  totalClicks = {clicks}" );
    }
    else
    {
        Fail( $"GET /api/links/{linkId}/stats", $"{(int)statsStatus}" );
    }

    var (statsNotFoundStatus, _) = await GET( "/api/links/99999/stats" );
    if ( statsNotFoundStatus == HttpStatusCode.NotFound )
        Pass( "GET /api/links/99999/stats  →  404 (correct)" );
    else
        Fail( "GET /api/links/99999/stats", $"expected 404, got {(int)statsNotFoundStatus}" );
}
else
{
    Fail( "Stats tests skipped", "no linkId available" );
}

Section( "Redirect" );

if ( shortUrl is not null )
{
    var (redirStatus, _) = await GET( $"/{shortUrl}", false );
    if ( redirStatus is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.PermanentRedirect
        or HttpStatusCode.TemporaryRedirect )
        Pass( $"GET /{shortUrl}  →  {(int)redirStatus} redirect to original URL" );
    else
        Fail( $"GET /{shortUrl}", $"expected 3xx, got {(int)redirStatus}" );
}
else
{
    Fail( "Redirect test skipped", "no shortUrl" );
}

Section( "Favourites" );

if ( linkId > 0 )
{
    var (favStatus, favBody) = await POST( $"/api/links/favourite?linkId={linkId}" );
    if ( favStatus == HttpStatusCode.OK )
        Pass( $"POST /api/links/favourite?linkId={linkId}  →  added" );
    else
        Fail( "POST /api/links/favourite", $"{(int)favStatus} {favBody}" );

    var (dupStatus, _) = await POST( $"/api/links/favourite?linkId={linkId}" );
    if ( dupStatus == HttpStatusCode.BadRequest )
        Pass( "POST /api/links/favourite  (duplicate)  →  400 (correct)" );
    else
        Fail( "POST /api/links/favourite (duplicate)", $"expected 400, got {(int)dupStatus}" );

    var (favsStatus, favsBody) = await GET( "/api/links/favourites" );
    if ( favsStatus == HttpStatusCode.OK && favsBody.HasValue && favsBody.Value.ValueKind == JsonValueKind.Array )
        Pass( $"GET /api/links/favourites  →  {favsBody.Value.GetArrayLength()} item(s)" );
    else
        Fail( "GET /api/links/favourites", $"{(int)favsStatus}" );

    var (unfavStatus, _) = await POST( $"/api/links/unfavourite?linkId={linkId}" );
    if ( unfavStatus == HttpStatusCode.OK )
        Pass( $"POST /api/links/unfavourite?linkId={linkId}  →  removed" );
    else
        Fail( "POST /api/links/unfavourite", $"{(int)unfavStatus}" );

    var (unfavAgainStatus, _) = await POST( $"/api/links/unfavourite?linkId={linkId}" );
    if ( unfavAgainStatus == HttpStatusCode.NotFound )
        Pass( "POST /api/links/unfavourite  (not in favourites)  →  404 (correct)" );
    else
        Fail( "POST /api/links/unfavourite (not in fav)", $"expected 404, got {(int)unfavAgainStatus}" );
}
else
{
    Fail( "Favourite tests skipped", "no linkId available" );
}

Section( "Categories" );

var catName = "TestCat_" + Guid.NewGuid().ToString( "N" )[..8];

var (catStatus, catBody) = await POST( $"/api/links/category?categoryName={catName}" );
if ( catStatus == HttpStatusCode.Created && catBody.HasValue )
{
    categoryId = catBody.Value.GetProperty( "id" ).GetInt32();
    Pass( $"POST /api/links/category  →  id = {categoryId}, name = {catName}" );
}
else
{
    Fail( "POST /api/links/category", $"{(int)catStatus} {catBody}" );
}

var (catDupStatus, _) = await POST( $"/api/links/category?categoryName={catName}" );
if ( catDupStatus == HttpStatusCode.BadRequest )
    Pass( "POST /api/links/category  (duplicate)  →  400 (correct)" );
else
    Fail( "POST /api/links/category (duplicate)", $"expected 400, got {(int)catDupStatus}" );

var (catsStatus, catsBody) = await GET( "/api/links/categories" );
if ( catsStatus == HttpStatusCode.OK && catsBody.HasValue && catsBody.Value.ValueKind == JsonValueKind.Array )
    Pass( $"GET /api/links/categories  →  {catsBody.Value.GetArrayLength()} item(s)" );
else
    Fail( "GET /api/links/categories", $"{(int)catsStatus}" );

if ( linkId > 0 && categoryId > 0 )
{
    var (assignStatus, assignBody) = await PUT( $"/api/links/{linkId}/category?categoryId={categoryId}" );
    if ( assignStatus == HttpStatusCode.OK )
        Pass( $"PUT /api/links/{linkId}/category?categoryId={categoryId}  →  assigned" );
    else
        Fail( "PUT /api/links/{id}/category", $"{(int)assignStatus} {assignBody}" );
    
    var (getAfterStatus, getAfterBody) = await GET( "/api/links/get" );
    if ( getAfterStatus == HttpStatusCode.OK && getAfterBody.HasValue )
        foreach ( var cat in from item in getAfterBody.Value.EnumerateArray() where item.GetProperty( "id" ).GetInt32() == linkId select item.GetProperty( "category" ).GetString() )
        {
            if ( cat == catName )
                Pass( $"GET /api/links/get  →  category = '{cat}' (correct)" );
            else
                Fail( "GET /api/links/get category check", $"expected '{catName}', got '{cat}'" );
            
            break;
        }
    
    var (unassignStatus, _) = await PUT( $"/api/links/{linkId}/category" );
    
    if ( unassignStatus == HttpStatusCode.OK )
        Pass( $"PUT /api/links/{linkId}/category  (no categoryId)  →  category removed" );
    else
        Fail( "PUT /api/links/{id}/category (remove)", $"{(int)unassignStatus}" );
}

if ( linkId > 0 )
{
    var (badCatStatus, _) = await PUT( $"/api/links/{linkId}/category?categoryId=99999" );
    if ( badCatStatus == HttpStatusCode.NotFound )
        Pass( "PUT /api/links/{id}/category?categoryId=99999  →  404 (correct)" );
    else
        Fail( "PUT /api/links/{id}/category (bad cat)", $"expected 404, got {(int)badCatStatus}" );
}

if ( categoryId > 0 )
{
    var (delCatStatus, _) = await DELETE( $"/api/links/category/{categoryId}" );
    if ( delCatStatus == HttpStatusCode.OK )
        Pass( $"DELETE /api/links/category/{categoryId}  →  deleted" );
    else
        Fail( $"DELETE /api/links/category/{categoryId}", $"{(int)delCatStatus}" );
}

Section( "Links — remove" );

if ( linkId > 0 )
{
    var (removeStatus, _) = await POST( $"/api/links/remove?linkId={linkId}" );
    if ( removeStatus == HttpStatusCode.OK )
        Pass( $"POST /api/links/remove?linkId={linkId}  →  deleted" );
    else
        Fail( "POST /api/links/remove", $"{(int)removeStatus}" );

    var (getAfterRemove, getBody2) = await GET( "/api/links/get" );
    if ( getAfterRemove == HttpStatusCode.OK && getBody2.HasValue )
    {
        var found = false;
        foreach ( var item in getBody2.Value.EnumerateArray() )
            if ( item.GetProperty( "id" ).GetInt32() == linkId )
            {
                found = true;
                break;
            }

        if ( !found )
            Pass( "GET /api/links/get after remove  →  link not in list (correct)" );
        else
            Fail( "GET /api/links/get after remove", "deleted link still visible" );
    }

    if ( shortUrl is not null )
    {
        var (deletedRedirStatus, _) = await GET( $"/{shortUrl}", false );
        if ( deletedRedirStatus == HttpStatusCode.NotFound )
            Pass( $"GET /{shortUrl} after remove  →  404 (correct)" );
        else
            Fail( $"GET /{shortUrl} after remove", $"expected 404, got {(int)deletedRedirStatus}" );
    }
    
    var (removeAgain, _) = await POST( $"/api/links/remove?linkId={linkId}" );
    if ( removeAgain == HttpStatusCode.NotFound )
        Pass( "POST /api/links/remove  (already deleted)  →  404 (correct)" );
    else
        Fail( "POST /api/links/remove (already deleted)", $"expected 404, got {(int)removeAgain}" );
}
else
{
    Fail( "Remove tests skipped", "no linkId available" );
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine( new string( '─', 45 ) );

Console.ForegroundColor = ConsoleColor.Green;
Console.Write( $"  PASSED: {passed}" );
Console.ForegroundColor = ConsoleColor.Red;
Console.Write( $"   FAILED: {failed}" );
Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
Console.WriteLine( $"   TOTAL: {passed + failed}" );
Console.ResetColor();
Console.WriteLine();

return;

void Info( string text )
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine( $"│    {text}" );
    Console.ResetColor();
}

void Fail( string name, string reason )
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write( "│  ✗ " );
    Console.ResetColor();
    Console.WriteLine( $"{name}  →  {reason}" );
    failed++;
}

void Pass( string name )
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write( "│  ✓ " );
    Console.ResetColor();
    Console.WriteLine( name );
    passed++;
}

void Section( string name )
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine( $"┌─ {name}" );
    Console.ResetColor();
}