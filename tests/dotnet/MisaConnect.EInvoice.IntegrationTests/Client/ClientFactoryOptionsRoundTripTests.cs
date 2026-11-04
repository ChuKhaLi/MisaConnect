using System.Reflection;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Client;

/// <summary>
/// Slice 009 US1 (FR-001, FR-002). <see cref="MisaEInvoiceClientFactory.Create(MisaEInvoiceOptions)"/>
/// used to copy only the six connection settings and drop <c>Delete</c>. These tests set every
/// public settable option (recursively) to a non-default value and require it to survive the copy,
/// so a property added later without a factory update fails here. <c>Create</c> exposes only
/// <see cref="IMisaEInvoiceClient"/>, so the copy step is tested directly through <c>CopyTo</c>.
/// No network.
/// </summary>
public class ClientFactoryOptionsRoundTripTests
{
    [Fact]
    public void CopyTo_carries_every_public_option_including_nested_ones()
    {
        var source = new MisaEInvoiceOptions();
        var counter = 0;
        Populate(source, ref counter);
        Assert.True(counter >= 7, $"expected at least 7 leaf options, found {counter}");

        var target = new MisaEInvoiceOptions();
        MisaEInvoiceClientFactory.CopyTo(source, target);

        AssertLeavesEqual(source, target, nameof(MisaEInvoiceOptions));
    }

    [Fact]
    public void CopyTo_copies_Delete_by_value_not_by_reference()
    {
        var source = new MisaEInvoiceOptions { Delete = { IncludeRawErrorMessage = true } };
        var target = new MisaEInvoiceOptions();

        MisaEInvoiceClientFactory.CopyTo(source, target);
        source.Delete.IncludeRawErrorMessage = false;

        Assert.NotSame(source.Delete, target.Delete);
        Assert.True(target.Delete.IncludeRawErrorMessage);
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

    private static bool IsLeaf(Type t) => t == typeof(string) || t == typeof(bool) || t.IsEnum || t.IsPrimitive;

    private static void Populate(object target, ref int counter)
    {
        foreach (var p in SettableProperties(target.GetType()))
        {
            var t = p.PropertyType;
            if (t == typeof(string))
            {
                p.SetValue(target, $"value-{p.Name}-{++counter}");
            }
            else if (t == typeof(bool))
            {
                p.SetValue(target, true);
                counter++;
            }
            else if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                Assert.True(values.Length > 1, $"{p.Name}: enum needs a non-default member to test");
                p.SetValue(target, values.GetValue(values.Length - 1));
                counter++;
            }
            else if (t.IsPrimitive)
            {
                p.SetValue(target, Convert.ChangeType(++counter, t, System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (t.IsClass)
            {
                var nested = p.GetValue(target) ?? Activator.CreateInstance(t)!;
                Populate(nested, ref counter);
                p.SetValue(target, nested);
            }
            else
            {
                Assert.Fail($"{p.Name}: unsupported option type {t.Name}; extend this test");
            }
        }
    }

    private static void AssertLeavesEqual(object expected, object actual, string path)
    {
        foreach (var p in SettableProperties(expected.GetType()))
        {
            var name = $"{path}.{p.Name}";
            var e = p.GetValue(expected);
            var a = p.GetValue(actual);
            if (IsLeaf(p.PropertyType))
            {
                Assert.True(Equals(e, a), $"{name} was not copied by MisaEInvoiceClientFactory (expected '{e}', got '{a}')");
            }
            else
            {
                Assert.NotNull(a);
                AssertLeavesEqual(e!, a!, name);
            }
        }
    }
}
