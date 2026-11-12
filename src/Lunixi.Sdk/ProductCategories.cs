namespace Lunixi;

/// <summary>
/// Lunixi product category codes for <see cref="BasketItem.ProductCategory"/>. Used by payment methods with legal
/// installment limits (e.g. shopping loan) and other platform features. The dictionary only grows by appending, so
/// the property stays a plain string and a newer code can be sent before this SDK declares a constant for it.
/// Unknown codes are rejected by the API with HTTP 400.
/// </summary>
public static class ProductCategories
{
    /// <summary><c>MOBILE_PHONE</c></summary>
    public const string MobilePhone = "MOBILE_PHONE";
    /// <summary><c>TABLET</c></summary>
    public const string Tablet = "TABLET";
    /// <summary><c>COMPUTER</c></summary>
    public const string Computer = "COMPUTER";
    /// <summary><c>TV_AUDIO</c></summary>
    public const string TvAudio = "TV_AUDIO";
    /// <summary><c>WHITE_GOODS</c></summary>
    public const string WhiteGoods = "WHITE_GOODS";
    /// <summary><c>SMALL_APPLIANCE</c></summary>
    public const string SmallAppliance = "SMALL_APPLIANCE";
    /// <summary><c>CAMERA</c></summary>
    public const string Camera = "CAMERA";
    /// <summary><c>GAME_CONSOLE</c></summary>
    public const string GameConsole = "GAME_CONSOLE";
    /// <summary><c>WEARABLE</c></summary>
    public const string Wearable = "WEARABLE";
    /// <summary><c>ELECTRONICS_ACCESSORY</c></summary>
    public const string ElectronicsAccessory = "ELECTRONICS_ACCESSORY";
    /// <summary><c>ELECTRONICS_OTHER</c></summary>
    public const string ElectronicsOther = "ELECTRONICS_OTHER";
    /// <summary><c>GOLD</c></summary>
    public const string Gold = "GOLD";
    /// <summary><c>JEWELRY</c></summary>
    public const string Jewelry = "JEWELRY";
    /// <summary><c>WATCH</c></summary>
    public const string Watch = "WATCH";
    /// <summary><c>APPAREL</c></summary>
    public const string Apparel = "APPAREL";
    /// <summary><c>SHOES_BAGS</c></summary>
    public const string ShoesBags = "SHOES_BAGS";
    /// <summary><c>COSMETICS</c></summary>
    public const string Cosmetics = "COSMETICS";
    /// <summary><c>FURNITURE</c></summary>
    public const string Furniture = "FURNITURE";
    /// <summary><c>HOME_LIVING</c></summary>
    public const string HomeLiving = "HOME_LIVING";
    /// <summary><c>GROCERY</c></summary>
    public const string Grocery = "GROCERY";
    /// <summary><c>BOOKS_MEDIA</c></summary>
    public const string BooksMedia = "BOOKS_MEDIA";
    /// <summary><c>TOYS</c></summary>
    public const string Toys = "TOYS";
    /// <summary><c>SPORTS_OUTDOOR</c></summary>
    public const string SportsOutdoor = "SPORTS_OUTDOOR";
    /// <summary><c>PET_SUPPLIES</c></summary>
    public const string PetSupplies = "PET_SUPPLIES";
    /// <summary><c>AUTO_PARTS</c></summary>
    public const string AutoParts = "AUTO_PARTS";
    /// <summary><c>MUSICAL_INSTRUMENT</c></summary>
    public const string MusicalInstrument = "MUSICAL_INSTRUMENT";
    /// <summary><c>MICROMOBILITY</c></summary>
    public const string Micromobility = "MICROMOBILITY";
    /// <summary><c>VEHICLE</c></summary>
    public const string Vehicle = "VEHICLE";
    /// <summary><c>TRANSPORT_TICKET</c></summary>
    public const string TransportTicket = "TRANSPORT_TICKET";
    /// <summary><c>EVENT_TICKET</c></summary>
    public const string EventTicket = "EVENT_TICKET";
    /// <summary><c>ACCOMMODATION</c></summary>
    public const string Accommodation = "ACCOMMODATION";
    /// <summary><c>TRAVEL_PACKAGE</c></summary>
    public const string TravelPackage = "TRAVEL_PACKAGE";
    /// <summary><c>HEALTH_SERVICE</c></summary>
    public const string HealthService = "HEALTH_SERVICE";
    /// <summary><c>EDUCATION</c></summary>
    public const string Education = "EDUCATION";
    /// <summary><c>SHIPPING_SERVICE</c></summary>
    public const string ShippingService = "SHIPPING_SERVICE";
    /// <summary><c>DIGITAL_GOODS</c></summary>
    public const string DigitalGoods = "DIGITAL_GOODS";
    /// <summary><c>GIFT_CARD</c></summary>
    public const string GiftCard = "GIFT_CARD";
    /// <summary><c>SUBSCRIPTION</c></summary>
    public const string Subscription = "SUBSCRIPTION";
    /// <summary><c>SERVICE_OTHER</c></summary>
    public const string ServiceOther = "SERVICE_OTHER";
    /// <summary><c>OTHER</c></summary>
    public const string Other = "OTHER";

    /// <summary>The codes known to this SDK version, in dictionary order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        MobilePhone,
        Tablet,
        Computer,
        TvAudio,
        WhiteGoods,
        SmallAppliance,
        Camera,
        GameConsole,
        Wearable,
        ElectronicsAccessory,
        ElectronicsOther,
        Gold,
        Jewelry,
        Watch,
        Apparel,
        ShoesBags,
        Cosmetics,
        Furniture,
        HomeLiving,
        Grocery,
        BooksMedia,
        Toys,
        SportsOutdoor,
        PetSupplies,
        AutoParts,
        MusicalInstrument,
        Micromobility,
        Vehicle,
        TransportTicket,
        EventTicket,
        Accommodation,
        TravelPackage,
        HealthService,
        Education,
        ShippingService,
        DigitalGoods,
        GiftCard,
        Subscription,
        ServiceOther,
        Other,
    });
}
