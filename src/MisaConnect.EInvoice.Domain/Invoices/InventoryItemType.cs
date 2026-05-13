namespace MisaConnect.EInvoice.Domain.Invoices;

/// <summary>
/// Mirrors the MISA CURL convention exactly (per data-model.md §3.4).
/// The formula-sheet's 1-based variant is NOT modelled in slice 1.
/// </summary>
public enum InventoryItemType
{
    Goods = 0,
    Promotion = 2,
    Note = 3,
    TradeDiscount = 4,
    SpecialGoods = 6,
}
