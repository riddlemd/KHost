namespace KHost.Abstractions.Models;

/// <summary>How a <see cref="Tip"/> was paid.</summary>
public enum TipPaymentMethod
{
    /// <summary>Paid in cash, in person.</summary>
    Cash = 0,
    /// <summary>Paid through PayPal.</summary>
    PayPal = 1,
    /// <summary>Paid through Cash App.</summary>
    CashApp = 2,
    /// <summary>Paid through Venmo.</summary>
    Venmo = 3,
    /// <summary>Paid through Zelle.</summary>
    Zelle = 4,
    /// <summary>Paid with Apple Pay.</summary>
    ApplePay = 5,
    /// <summary>Paid with Google Pay.</summary>
    GooglePay = 6,
    /// <summary>Paid through a Stripe checkout.</summary>
    Stripe = 7,
    /// <summary>Paid through a Square reader or checkout.</summary>
    Square = 8,

    /// <summary>Any method not otherwise listed.</summary>
    Other = 9
}
