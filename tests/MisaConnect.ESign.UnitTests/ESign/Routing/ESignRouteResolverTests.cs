using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.ESign;
using Xunit;

namespace MisaConnect.ESign.UnitTests.ESign.Routing;

public class ESignRouteResolverTests
{
    // ---- Origin normalization (Defect A + C: base path is discarded) ----

    [Theory]
    [InlineData("https://esignapp.misa.vn/", "https://esignapp.misa.vn/")]
    [InlineData("https://esignapp.misa.vn/webdev/", "https://esignapp.misa.vn/")]
    [InlineData("https://esignapp.misa.vn/a/b/c/", "https://esignapp.misa.vn/")]
    [InlineData("https://host:8443/webdev/", "https://host:8443/")]
    [InlineData("http://127.0.0.1:5005/webdev/", "http://127.0.0.1:5005/")]
    public void Origin_strips_any_path_and_keeps_scheme_host_port(string baseUrl, string expected)
    {
        Assert.Equal(expected, ESignRouteResolver.Origin(baseUrl).ToString());
    }

    // ---- EffectiveAuthUnderWebdev derivation (data-model §1) ----

    [Fact]
    public void Effective_null_override_derives_true_for_sandbox()
    {
        var o = new MisaESignOptions { Environment = ESignEnvironment.Sandbox, AuthUnderWebdev = null };
        Assert.True(ESignRouteResolver.EffectiveAuthUnderWebdev(o));
    }

    [Fact]
    public void Effective_null_override_derives_false_for_production()
    {
        var o = new MisaESignOptions { Environment = ESignEnvironment.Production, AuthUnderWebdev = null };
        Assert.False(ESignRouteResolver.EffectiveAuthUnderWebdev(o));
    }

    [Theory]
    [InlineData(ESignEnvironment.Production, true, true)]
    [InlineData(ESignEnvironment.Sandbox, false, false)]
    public void Effective_explicit_override_wins(ESignEnvironment env, bool over, bool expected)
    {
        var o = new MisaESignOptions { Environment = env, AuthUnderWebdev = over };
        Assert.Equal(expected, ESignRouteResolver.EffectiveAuthUnderWebdev(o));
    }

    // ---- ResolveRequestPath: login/two-factor flip; everything else identity ----

    [Theory]
    [InlineData(ESignHttpRoutes.AuthLoginApi)]
    [InlineData(ESignHttpRoutes.AuthTwoFactor)]
    public void Login_and_two_factor_are_unchanged_at_root(string route)
    {
        Assert.Equal(route, ESignRouteResolver.ResolveRequestPath(route, authUnderWebdev: false));
    }

    [Theory]
    [InlineData(ESignHttpRoutes.AuthLoginApi)]
    [InlineData(ESignHttpRoutes.AuthTwoFactor)]
    public void Login_and_two_factor_get_webdev_prefix_when_enabled(string route)
    {
        Assert.Equal("webdev/" + route, ESignRouteResolver.ResolveRequestPath(route, authUnderWebdev: true));
    }

    [Theory]
    [InlineData(ESignHttpRoutes.AuthRefreshToken)]
    [InlineData(ESignHttpRoutes.AuthResendOtp)]
    [InlineData(ESignHttpRoutes.CertificatesByUserId)]
    [InlineData(ESignHttpRoutes.DocumentsHash)]
    [InlineData(ESignHttpRoutes.SigningHash)]
    [InlineData(ESignHttpRoutes.SigningStatus)]
    [InlineData(ESignHttpRoutes.DocumentsAttachment)]
    public void Non_login_routes_are_identity_regardless_of_flag(string route)
    {
        Assert.Equal(route, ESignRouteResolver.ResolveRequestPath(route, authUnderWebdev: false));
        Assert.Equal(route, ESignRouteResolver.ResolveRequestPath(route, authUnderWebdev: true));
    }

    // ---- Resolved absolute URLs: ESRM at host root regardless of base path (Contract C1) ----

    [Theory]
    [InlineData("https://esignapp.misa.vn/")]
    [InlineData("https://esignapp.misa.vn/webdev/")]
    public void Esrm_resolves_to_host_root_for_any_base_path(string baseUrl)
    {
        var origin = ESignRouteResolver.Origin(baseUrl);
        var path = ESignRouteResolver.ResolveRequestPath(ESignHttpRoutes.CertificatesByUserId, authUnderWebdev: true);
        var absolute = new Uri(origin, path);
        Assert.Equal(
            "https://esignapp.misa.vn/external/esrm/service/general/api/v1/Certificates/by-userId",
            absolute.ToString());
    }

    [Theory]
    [InlineData("https://esignapp.misa.vn/")]
    [InlineData("https://esignapp.misa.vn/webdev/")]
    public void Refresh_resolves_to_single_webdev_segment_for_any_base_path(string baseUrl)
    {
        var origin = ESignRouteResolver.Origin(baseUrl);
        var path = ESignRouteResolver.ResolveRequestPath(ESignHttpRoutes.AuthRefreshToken, authUnderWebdev: true);
        var absolute = new Uri(origin, path);
        Assert.Equal(
            "https://esignapp.misa.vn/webdev/api/auth/api/v1/auth/refreshtoken",
            absolute.ToString());
    }

    // ---- Resolved login URL flips with the flag (Contract C3) ----

    [Fact]
    public void Login_resolves_at_root_for_production()
    {
        var origin = ESignRouteResolver.Origin("https://esignapp.misa.vn/");
        var path = ESignRouteResolver.ResolveRequestPath(ESignHttpRoutes.AuthLoginApi, authUnderWebdev: false);
        Assert.Equal(
            "https://esignapp.misa.vn/api/auth/api/v1/auth/login-api",
            new Uri(origin, path).ToString());
    }

    [Fact]
    public void Login_resolves_under_webdev_for_sandbox()
    {
        var origin = ESignRouteResolver.Origin("https://esignapp.misa.vn/webdev/");
        var path = ESignRouteResolver.ResolveRequestPath(ESignHttpRoutes.AuthLoginApi, authUnderWebdev: true);
        Assert.Equal(
            "https://esignapp.misa.vn/webdev/api/auth/api/v1/auth/login-api",
            new Uri(origin, path).ToString());
    }
}
