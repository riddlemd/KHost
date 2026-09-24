namespace KHost.Abstractions.Models;

/// <summary>How a <see cref="Tip"/> was paid.</summary>
public enum TipPaymentMethod
{
    /// <summary>Paid in cash, in person.</summary>
    Cash,
    /// <summary>Paid through PayPal.</summary>
    PayPal,
    /// <summary>Paid through Cash App.</summary>
    CashApp,
    /// <summary>Paid through Venmo.</summary>
    Venmo,
    /// <summary>Paid through Zelle.</summary>
    Zelle,
    /// <summary>Paid with Apple Pay.</summary>
    ApplePay,
    /// <summary>Paid with Google Pay.</summary>
    GooglePay,
    /// <summary>Paid through a Stripe checkout.</summary>
    Stripe,
    /// <summary>Paid through a Square reader or checkout.</summary>
    Square,

    /// <summary>Any method not otherwise listed.</summary>
    Other
}
