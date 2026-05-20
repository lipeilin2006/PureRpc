# PureRpc.Authentication.JwtBearer

JWT Bearer authentication middleware for PureRpc. Validates JWT tokens from the `Authorization` header and sets `context.User`.

## Usage

### With TokenValidationParameters (symmetric key):

```
builder.AddPureRpcServer()
    .AddJwtBearerAuthentication(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "my-issuer",
            ValidAudience = "my-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secret-key"))
        };
    });
```

### With OIDC Authority (Azure AD, IdentityServer, etc.):

```
var jwtOptions = new JwtBearerOptions
{
    Authority = "https://login.microsoftonline.com/{tenant}/v2.0",
    Audience = "api://my-api",
};
await jwtOptions.UseAuthorityAsync();

builder.AddPureRpcServer()
    .AddJwtBearerAuthentication(jwtOptions);
```
