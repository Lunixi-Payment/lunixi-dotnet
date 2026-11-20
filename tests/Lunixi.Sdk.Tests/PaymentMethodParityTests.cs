using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lunixi.Sdk.Tests;

public class PaymentMethodParityTests
{
    private sealed record Recorded(string Method, string PathAndQuery, string? Body, bool Signed, string? IdempotencyKey, string Accept);

    private static (LunixiClient Client, List<Recorded> Requests) Create(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var requests = new List<Recorded>();
        var client = new LunixiClient(TestOptions.Create(retries: 0), new FakeHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/token") return FakeHandler.Json("{\"access_token\":\"token\"}");
            requests.Add(new(request.Method.Method, request.RequestUri.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct),
                request.Headers.Contains("X-Signature") && request.Headers.Contains("X-Key-Id"),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                string.Join(", ", request.Headers.Accept.Select(a => a.MediaType))));
            return respond?.Invoke(request) ?? FakeHandler.Json();
        }));
        return (client, requests);
    }

    private static DirectMethodPaymentRequest Order(string amount = "10000") => new DirectMethodPaymentRequest(amount, "TRY", "ORDER-1")
        .WithBuyer(TestOrder.Buyer).WithBillingAddress(TestOrder.Address).WithBasketItems(TestOrder.Basket(amount));

    private static object[] Basket(int count) => Enumerable.Range(1, count)
        .Select(i => (object)new { id = "sku-" + i, name = "Item", category1 = "Electronics", itemType = "PHYSICAL", price = "1" }).ToArray();

    [Fact]
    public void ConstantsMatchTheReferenceValues()
    {
        Assert.Equal(new[] { "CARD", "BNPL", "SHOPPING_LOAN", "PROMISSORY_NOTE", "MEAL_CARD", "GIFT_CARD", "CASH", "CASH_ON_DELIVERY", "WALLET", "BANK_TRANSFER", "OPEN_BANKING", "MOBILE_BILLING", "QR_CODE", "CRYPTO", "OTHER" }, PaymentMethods.All);
        Assert.Equal("QR", PaymentMethods.QrAlias);
        Assert.Equal(100, PaymentMethods.ShoppingLoanMaxBasketItems);
        Assert.Equal(new[] { "OTP", "REDIRECT", "DISPLAY", "APP_APPROVAL", "INPUT" }, new[] { PaymentActionTypes.Otp, PaymentActionTypes.Redirect, PaymentActionTypes.Display, PaymentActionTypes.AppApproval, PaymentActionTypes.Input });
        Assert.Equal(new[] { "ROUTED", "OPTIONAL", "REQUIRED" }, new[] { ProviderSelections.Routed, ProviderSelections.Optional, ProviderSelections.Required });
        Assert.Equal(new[] { "BUYER", "PLATFORM" }, new[] { ProviderSelectionModes.Buyer, ProviderSelectionModes.Platform });
        Assert.Equal(new[] { "mobileBilling", "wallet", "promissoryNote", "mealCard", "openBanking", "shoppingLoan", "bankTransfer" }, PaymentMethodFields.MethodBlocks);
        Assert.Equal(27, PaymentMethodFields.DirectMethodPayment.Count);
        Assert.Equal(PaymentMethodFields.MethodBlocks, PaymentMethodFields.DirectMethodPayment.TakeLast(7));
        Assert.Equal(new[] { "card", "installment", "installmentQuoteToken", "force3D" }, PaymentMethodFields.CardOnly);
        Assert.Equal(new[] { "otp", "consentAccepted", "fields" }, PaymentMethodFields.ActionConfirm);
        Assert.Equal(new[] { "mobileBilling", "wallet", "promissoryNote" }, SplitPaymentFields.LegBlocks);
        Assert.Equal(new[] { "status", "currency", "createdFrom", "createdTo", "orderId", "ownerRef", "limit", "after" }, SplitPaymentFields.ListFilters);

        Assert.Equal("9999", BankTransferValues.OtherBanksCode);
        Assert.Equal(200, BankTransferValues.ExceptionLimitMax);
        Assert.Equal(new[] { "LATE", "UNDERPAID", "OVERPAID" }, new[] { BankTransferExceptionKinds.Late, BankTransferExceptionKinds.Underpaid, BankTransferExceptionKinds.Overpaid });
        Assert.Equal(new[] { "PENDING_DECISION", "ACCEPTED", "REFUND_PENDING", "REFUNDED" }, BankTransferExceptionStatuses.All);

        Assert.Equal("payment.bank_transfer.partially_received", PaymentBankTransferEvents.PartiallyReceived);
        Assert.Equal("payment.bank_transfer.underpaid", PaymentBankTransferEvents.Underpaid);
        Assert.Equal("payment.bank_transfer.overpaid", PaymentBankTransferEvents.Overpaid);
        Assert.Equal("payment.bank_transfer.late_arrival", PaymentBankTransferEvents.LateArrival);
        Assert.Equal(new[] { "payment.promissory_note.paid", "payment.promissory_note.protested", "payment.promissory_note.cancelled" }, new[] { PromissoryNoteEvents.Paid, PromissoryNoteEvents.Protested, PromissoryNoteEvents.Cancelled });
        Assert.Equal(new[] { "payment.split.completed", "payment.split.voided", "payment.split.compensating", "payment.split.outcome_pending", "payment.split.reconciliation_required", "payment.split.leg_refund_pending" },
            new[] { SplitPaymentEvents.Completed, SplitPaymentEvents.Voided, SplitPaymentEvents.Compensating, SplitPaymentEvents.OutcomePending, SplitPaymentEvents.ReconciliationRequired, SplitPaymentEvents.LegRefundPending });
        Assert.Equal("payment.split.leg_events", SplitPaymentEvents.LegEventsSubscription);

        Assert.Equal(24, PromissoryNotePlan.MaxNotes);
        Assert.Equal(new[] { "ISSUED", "PAID", "PROTESTED", "CANCELLED" }, new[] { PromissoryNoteStatuses.Issued, PromissoryNoteStatuses.Paid, PromissoryNoteStatuses.Protested, PromissoryNoteStatuses.Cancelled });
        Assert.Equal(new[] { "PROMISSORY_NOTE", "PAYMENT_ORDER" }, new[] { PromissoryNoteDocumentTypes.PromissoryNote, PromissoryNoteDocumentTypes.PaymentOrder });
        Assert.Equal(new[] { "CANCELLED", "ALREADY_CLOSED", "NOT_CANCELLABLE", "FAILED" }, new[] { PromissoryNoteCancelResults.Cancelled, PromissoryNoteCancelResults.AlreadyClosed, PromissoryNoteCancelResults.NotCancellable, PromissoryNoteCancelResults.Failed });
        Assert.Equal(new[] { "payment.promissory_note.not_cancellable", "payment.promissory_note.provider_unavailable", "payment.promissory_note.cancel_rejected" }, new[] { PromissoryNoteCancelErrorCodes.NotCancellable, PromissoryNoteCancelErrorCodes.ProviderUnavailable, PromissoryNoteCancelErrorCodes.CancelRejected });
        Assert.Equal(new[] { "tr-TR", "en-GB" }, new[] { PromissoryNoteDocumentLocales.Turkish, PromissoryNoteDocumentLocales.English });
    }

    [Fact]
    public async Task BankTransferBodyCarriesOnlyTheBankCodeAndIsSigned()
    {
        var (client, requests) = Create(_ => FakeHandler.Json("{\"data\":{\"paymentId\":\"pay-1\",\"status\":\"PENDING\"}}", HttpStatusCode.Accepted));
        using (client)
        {
            var response = await client.Payments.DirectMethodPaymentAsync(Order().WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = "0062" }), "direct:attempt-0001");
            Assert.Equal(202, response.StatusCode);
            var request = Assert.Single(requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/api/v1/payments/direct/payment", request.PathAndQuery);
            Assert.StartsWith("{\"paymentMethod\":\"BANK_TRANSFER\",\"amount\":\"10000\",\"currency\":\"TRY\",\"orderId\":\"ORDER-1\",\"buyer\":{", request.Body);
            Assert.EndsWith(",\"bankTransfer\":{\"bankCode\":\"0062\"}}", request.Body);
            Assert.DoesNotContain("providerCode", request.Body);
            Assert.True(request.Signed);
            Assert.Equal("direct:attempt-0001", request.IdempotencyKey);

            var otherBanks = Order().WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = BankTransferValues.OtherBanksCode }).ToPayload();
            Assert.Equal("9999", otherBanks.GetProperty("bankTransfer").GetProperty("bankCode").GetString());
        }
    }

    [Fact]
    public async Task BankTransferProviderCodeIsRejectedLocally()
    {
        var (client, requests) = Create();
        using (client)
        {
            Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.BankTransfer, "bank-provider", new { bankCode = "0062" }));
            Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = "0062", providerCode = "bank-provider" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.DirectMethodPaymentAsync(new
            {
                paymentMethod = "BANK_TRANSFER",
                amount = "10000",
                currency = "TRY",
                orderId = "ORDER-1",
                bankTransfer = new { providerCode = "bank-provider", bankCode = "0062" },
            }, "direct:attempt-0001"));
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task InvalidRawBodiesAndMissingKeysAreRejectedBeforeAnyRequest()
    {
        var (client, requests) = Create();
        using (client)
        {
            var payments = client.Payments;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "WALLET", amount = "100", currency = "TRY", orderId = "O-1", wallet = new { }, mobileBilling = new { } }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "WALLET", amount = "100", currency = "TRY", orderId = "O-1", wallet = new { }, installment = 1 }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "WALLET", amount = "100", currency = "TRY", orderId = "O-1", wallet = new { }, unknownField = "x" }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "MOBILE_BILLING", amount = "100", currency = "TRY", orderId = "O-1" }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "WALLET", amount = "100", price = "100", currency = "TRY", orderId = "O-1", wallet = new { } }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "GIFT_CARD", amount = "100", currency = "TRY", orderId = "O-1" }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(new { paymentMethod = "WALLET", currency = "TRY", orderId = "O-1", wallet = new { } }, "direct:attempt-0001"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1"), null!));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DirectMethodPaymentAsync(Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1"), ""));
            Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.GiftCard, null));
            Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Card, null));
            Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1").WithForce3D(true).ToPayload());
            Assert.Throws<LunixiConfigurationException>(() => Order().ToPayload());
            Assert.Empty(requests);
        }
    }

    [Fact]
    public async Task ShoppingLoanBasketMustHaveOneToOneHundredItems()
    {
        var (client, requests) = Create();
        using (client)
        {
            Task<LunixiResponse> Send(int items, string amount) => client.Payments.DirectMethodPaymentAsync(new
            {
                paymentMethod = "SHOPPING_LOAN",
                amount,
                currency = "TRY",
                orderId = "O-1",
                shoppingLoan = new { bankCode = "0062" },
                buyer = TestOrder.Buyer,
                billingAddress = TestOrder.Address,
                shippingAddress = TestOrder.Address,
                basketItems = Basket(items),
            }, "direct:attempt-0001");
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Send(0, "100"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => Send(101, "101"));
            Assert.Throws<LunixiConfigurationException>(() => new DirectMethodPaymentRequest("10000", "TRY", "ORDER-1")
                .WithBuyer(TestOrder.Buyer).WithBillingAddress(TestOrder.Address)
                .WithMethodFields(PaymentMethods.ShoppingLoan, null, new { bankCode = "0062" }).ToPayload());
            Assert.Empty(requests);
            await Send(100, "100");
            using var sent = JsonDocument.Parse(Assert.Single(requests).Body!);
            Assert.Equal(100, sent.RootElement.GetProperty("basketItems").GetArrayLength());
        }
        var typed = Order("2").WithMethodFields(PaymentMethods.ShoppingLoan, null, new { bankCode = "0062" })
            .WithShippingAddress(TestOrder.Address)
            .WithBasketItems([new BasketItem("sku-1", "Phone", "2") { Category1 = "Electronics", ProductCategory = ProductCategories.MobilePhone }]).ToPayload();
        Assert.Equal("MOBILE_PHONE", typed.GetProperty("basketItems")[0].GetProperty("productCategory").GetString());
    }

    [Fact]
    public async Task PromissoryNotePlanIsValidated()
    {
        var (client, requests) = Create();
        using var owned = client;
        static DirectMethodPaymentRequest Note() => Order().WithMethodFields(PaymentMethods.PromissoryNote, "note-provider", new { debtorName = "Synthetic Buyer" });
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.PromissoryNote, "note-provider", new { noteCount = "2" })
            .WithPromissoryNoteSchedule([new("10000", "2027-01-15")]).ToPayload());
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.DirectMethodPaymentAsync(new
        {
            paymentMethod = "PROMISSORY_NOTE",
            amount = "10000",
            currency = "TRY",
            orderId = "O-1",
            promissoryNote = new { noteCount = "2", schedule = new[] { new { amount = "10000", dueDate = "2027-01-15" } } },
        }, "direct:attempt-0001"));
        await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.DirectMethodPaymentAsync(new
        {
            paymentMethod = "PROMISSORY_NOTE",
            amount = "10000",
            currency = "TRY",
            orderId = "O-1",
            promissoryNote = new { providerCode = "note-provider" },
        }, "direct:attempt-0001"));
        Assert.Empty(requests);
        Assert.Throws<LunixiConfigurationException>(() => Note().ToPayload());
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteSchedule([new("5000", "2027-01-15"), new("4000", "2027-02-15")]).ToPayload());
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteSchedule([new("5000", "2027-01-15"), new("5000", "2027-02-30")]));
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteSchedule([new("5000", "2027-02-15"), new("5000", "2027-01-15")]));
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteSchedule(Enumerable.Range(1, 25).Select(i => new PromissoryNoteScheduleItem("400", "2027-01-15"))));
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteSchedule([new("0", "2027-01-15")]));
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteCount(25));
        Assert.Throws<LunixiConfigurationException>(() => Note().WithPromissoryNoteCount("03"));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1").WithPromissoryNoteCount(2).ToPayload());

        var schedule = Note().WithPromissoryNoteSchedule([new("5000", "2027-01-15"), new("5000", "2027-01-15")]).ToPayload();
        var scheduleJson = Encoding.UTF8.GetString(CanonicalJson.Serialize(schedule));
        Assert.StartsWith("{\"paymentMethod\":\"PROMISSORY_NOTE\",\"amount\":\"10000\",\"currency\":\"TRY\",\"orderId\":\"ORDER-1\",\"buyer\":{", scheduleJson);
        Assert.EndsWith(",\"promissoryNote\":{\"providerCode\":\"note-provider\",\"debtorName\":\"Synthetic Buyer\",\"schedule\":[{\"amount\":\"5000\",\"dueDate\":\"2027-01-15\"},{\"amount\":\"5000\",\"dueDate\":\"2027-01-15\"}]}}",
            scheduleJson);
        Assert.Equal("3", Note().WithPromissoryNoteCount(3).ToPayload().GetProperty("promissoryNote").GetProperty("noteCount").GetString());
        await client.Payments.DirectMethodPaymentAsync(new
        {
            paymentMethod = "PROMISSORY_NOTE",
            amount = "10000",
            currency = "TRY",
            orderId = "O-1",
            promissoryNote = new { providerCode = "note-provider", noteCount = 4 },
            buyer = TestOrder.Buyer,
            billingAddress = TestOrder.Address,
            basketItems = TestOrder.BasketJson("10000"),
        }, "direct:attempt-0001");
        var sent = Assert.Single(requests).Body;
        Assert.StartsWith("{\"paymentMethod\":\"PROMISSORY_NOTE\",\"amount\":\"10000\",\"currency\":\"TRY\",\"orderId\":\"O-1\",\"promissoryNote\":{\"providerCode\":\"note-provider\",\"noteCount\":\"4\"},\"buyer\":{", sent);
        Assert.EndsWith(",\"basketItems\":[{\"id\":\"SKU-1\",\"name\":\"Gift card\",\"category1\":\"Digital\",\"itemType\":\"VIRTUAL\",\"price\":\"10000\"}]}", sent);
    }

    [Fact]
    public void MethodBlockStructureIsValidated()
    {
        var tooMany = Enumerable.Range(1, 33).ToDictionary(i => "field" + i, _ => (object)"x");
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", tooMany));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", new { note = new string('x', 1025) }));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", new { guarantors = Enumerable.Range(1, 11).Select(i => new { name = "G" + i }).ToArray() }));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", new Dictionary<string, object> { ["__proto__"] = "x" }));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", new { address = Enumerable.Range(1, 17).ToDictionary(i => "part" + i, _ => "x") }));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet 1"));
        Assert.Throws<LunixiConfigurationException>(() => Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", new { nested = new { deeper = new { value = "x" } } }));

        var valid = Order().WithMethodFields(PaymentMethods.MobileBilling, "mobile-1", new
        {
            gsmNumber = "+905000000000",
            consent = true,
            count = 2,
            address = new { provinceCode = "06", districtCode = "1234", neighborhoodCode = "5678", line = "Synthetic street 1" },
            guarantors = new[] { new { name = "Synthetic", address = new { provinceCode = "34" } } },
        }).ToPayload();
        Assert.Equal("mobile-1", valid.GetProperty("mobileBilling").GetProperty("providerCode").GetString());
        Assert.Equal(Enumerable.Range(1, 32).ToDictionary(i => "field" + i, _ => "x").Count,
            Order().WithMethodFields(PaymentMethods.Wallet, "wallet-1", Enumerable.Range(1, 32).ToDictionary(i => "field" + i, _ => (object)"x")).ToPayload().GetProperty("wallet").EnumerateObject().Count() - 1);
    }

    [Fact]
    public async Task ConfirmAcceptsOnlyMerchantFieldsAndUsesTheActionPath()
    {
        var (client, requests) = Create();
        using (client)
        {
            var payments = client.Payments;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { otp = "123456", extra = "x" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { token = "checkout-token" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { checkoutSessionProof = "proof" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { otp = "12a456" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { otp = "12345678901234567" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { consentAccepted = "yes" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync("pay-1", "act-1", new { fields = new { providerCode = "x" } }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ConfirmActionAsync(" ", "act-1", new { otp = "123456" }));
            Assert.Empty(requests);

            await payments.ConfirmActionAsync("pay/1", "act-1", new { otp = "123456", consentAccepted = true, fields = new { tckn = "10000000146" } });
            await payments.ResendActionAsync("pay-1", "act-1");
            Assert.Equal("/api/v1/payments/pay%2F1/actions/act-1/confirm", requests[0].PathAndQuery);
            Assert.Equal("{\"otp\":\"123456\",\"consentAccepted\":true,\"fields\":{\"tckn\":\"10000000146\"}}", requests[0].Body);
            Assert.Equal("/api/v1/payments/pay-1/actions/act-1/resend", requests[1].PathAndQuery);
            Assert.All(requests, r => Assert.True(r.Signed));
        }
    }

    [Fact]
    public async Task BankTransferExceptionFiltersAndPathsAreValidated()
    {
        var (client, requests) = Create(_ => FakeHandler.Json("{\"data\":{\"items\":[{\"id\":\"9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f\",\"amountMinor\":\"250\"}]}}"));
        using (client)
        {
            var payments = client.Payments;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ListBankTransferExceptionsAsync(new() { Environment = "PROD" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ListBankTransferExceptionsAsync(new() { Limit = 0 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ListBankTransferExceptionsAsync(new() { Limit = 201 }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.ListBankTransferExceptionsAsync(new() { Status = "OPEN" }));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.AcceptBankTransferExceptionAsync("not-a-uuid"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.MarkBankTransferExceptionRefundedAsync("{9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f}"));
            Assert.Empty(requests);

            await payments.ListBankTransferExceptionsAsync();
            await payments.ListBankTransferExceptionsAsync(new() { Environment = "LIVE", Status = BankTransferExceptionStatuses.PendingDecision, Limit = 50 });
            await payments.AcceptBankTransferExceptionAsync("9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f");
            await payments.MarkBankTransferExceptionRefundedAsync("9F1C2D3E-4B5A-4C6D-8E7F-0A1B2C3D4E5F");
            Assert.Equal("/api/v1/payments/bank-transfer/exceptions", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/payments/bank-transfer/exceptions?environment=LIVE&status=PENDING_DECISION&limit=50", requests[1].PathAndQuery);
            Assert.Equal("/api/v1/payments/bank-transfer/exceptions/9f1c2d3e-4b5a-4c6d-8e7f-0a1b2c3d4e5f/accept", requests[2].PathAndQuery);
            Assert.Equal("/api/v1/payments/bank-transfer/exceptions/9F1C2D3E-4B5A-4C6D-8E7F-0A1B2C3D4E5F/refunded", requests[3].PathAndQuery);
            Assert.Null(requests[2].Body); Assert.Null(requests[3].Body);
            Assert.Equal(new[] { "GET", "GET", "POST", "POST" }, requests.Select(r => r.Method));
            Assert.All(requests, r => Assert.True(r.Signed));
        }
    }

    [Fact]
    public async Task MethodListNormalizesTheQrAliasAndRejectsLowercase()
    {
        var (client, requests) = Create();
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.ListMethodsAsync("mobile_billing"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Payments.ListMethodsAsync("_CARD"));
            Assert.Empty(requests);
            await client.Payments.ListMethodsAsync("QR");
            await client.Payments.ListMethodsAsync();
            Assert.Equal("/api/v1/payments/methods?paymentMethod=QR_CODE", requests[0].PathAndQuery);
            Assert.Equal("/api/v1/payments/methods", requests[1].PathAndQuery);
            Assert.All(requests, r => Assert.True(r.Signed));
        }
    }

    [Fact]
    public async Task PromissoryNoteEndpointsUseSignedRequestsAndReturnPdfBytes()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 synthetic document");
        var (client, requests) = Create(request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/document", StringComparison.Ordinal))
                return FakeHandler.Json("{\"data\":{\"totalAmount\":\"10000\",\"notes\":[{\"serialNumber\":\"SN-0001\",\"amount\":\"10000\",\"status\":\"ISSUED\"}]}}");
            var content = new ByteArrayContent(pdf);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using (client)
        {
            var payments = client.Payments;
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.DownloadPromissoryNoteDocumentAsync("pay-1", "de-DE"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.CancelPromissoryNotesAsync("pay-1", ["ab"]));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.CancelPromissoryNotesAsync("pay-1", ["SN 0001"]));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.CancelPromissoryNotesAsync("pay-1", []));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.CancelPromissoryNotesAsync("pay-1", Enumerable.Range(1000, 25).Select(i => "SN-" + i).ToArray()));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => payments.GetPromissoryNotesAsync(""));
            Assert.Empty(requests);

            var document = await payments.DownloadPromissoryNoteDocumentAsync("pay-1", PromissoryNoteDocumentLocales.English);
            Assert.Equal(pdf, document.ToArray());
            Assert.Equal("application/pdf", document.ContentType);
            Assert.Equal(200, document.StatusCode);
            await payments.DownloadPromissoryNoteDocumentAsync("pay-1");
            await payments.GetPromissoryNotesAsync("pay-1");
            await payments.CancelPromissoryNotesAsync("pay-1", ["SN-0001"]);
            await payments.CancelPromissoryNotesAsync("pay-1");

            Assert.Equal("/api/v1/payments/pay-1/promissory-notes/document?locale=en-GB", requests[0].PathAndQuery);
            Assert.Equal("GET", requests[0].Method);
            Assert.Contains("application/pdf", requests[0].Accept);
            Assert.Contains("application/json", requests[0].Accept);
            Assert.Equal("/api/v1/payments/pay-1/promissory-notes/document", requests[1].PathAndQuery);
            Assert.Equal("/api/v1/payments/pay-1/promissory-notes", requests[2].PathAndQuery);
            Assert.Equal("/api/v1/payments/pay-1/promissory-notes/cancel", requests[3].PathAndQuery);
            Assert.Equal("{\"serialNumbers\":[\"SN-0001\"]}", requests[3].Body);
            Assert.Null(requests[4].Body);
            Assert.All(requests, r => Assert.True(r.Signed));
        }
    }

    [Fact]
    public async Task PromissoryNoteDocumentErrorsAreReadAsTheJsonEnvelope()
    {
        var (client, _) = Create(_ => FakeHandler.Json("{\"code\":\"payment.promissory_note.document_unavailable\"}", HttpStatusCode.ServiceUnavailable));
        using (client)
        {
            var error = await Assert.ThrowsAsync<LunixiApiException>(() => client.Payments.DownloadPromissoryNoteDocumentAsync("pay-1"));
            Assert.Equal(503, error.StatusCode);
            Assert.Equal("payment.promissory_note.document_unavailable", error.Code);
        }
        var (jsonClient, _) = Create(_ => FakeHandler.Json("{\"data\":{}}"));
        using (jsonClient)
            await Assert.ThrowsAsync<LunixiTransportException>(() => jsonClient.Payments.DownloadPromissoryNoteDocumentAsync("pay-1"));
    }

    [Fact]
    public void SplitLegsRejectMethodsThatCannotBeSplit()
    {
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = "100", currency = "TRY", ownerRef = "owner", legs = new[] { new { paymentMethod = "BANK_TRANSFER", amount = "100" } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = "100", currency = "TRY", ownerRef = "owner", legs = new[] { new { paymentMethod = "OPEN_BANKING", amount = "100" } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = "100", currency = "TRY", ownerRef = "owner", legs = new object[] { new { paymentMethod = "BANK_TRANSFER", amount = "100", bankTransfer = new { bankCode = "0062" } } } }));
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new { totalAmount = "100", currency = "TRY", ownerRef = "owner", legs = new object[] { new { paymentMethod = "MEAL_CARD", amount = "100", mealCard = new { providerCode = "meal-1" } } } }));
        var notes = new SplitPaymentRequest(new
        {
            totalAmount = "100",
            currency = "TRY",
            ownerRef = "owner",
            legs = new object[] { new { paymentMethod = "PROMISSORY_NOTE", amount = "100", promissoryNote = new { providerCode = "note-1", noteCount = "3" } } },
            buyer = TestOrder.Buyer,
            billingAddress = TestOrder.Address,
            basketItems = TestOrder.BasketJson("100"),
        });
        Assert.Equal("3", notes.Payload.GetProperty("legs")[0].GetProperty("promissoryNote").GetProperty("noteCount").GetString());
        Assert.Throws<LunixiConfigurationException>(() => new SplitPaymentRequest(new
        {
            totalAmount = "100",
            currency = "TRY",
            ownerRef = "owner",
            legs = new object[] { new { paymentMethod = "PROMISSORY_NOTE", amount = "100", promissoryNote = new { providerCode = "note-1", noteCount = 25 } } },
        }));
    }

    [Fact]
    public async Task ReferenceAddressCodesAreValidated()
    {
        var (client, requests) = Create();
        using (client)
        {
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Reference.ListDistrictsAsync("82"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Reference.ListDistrictsAsync("00"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Reference.ListDistrictsAsync("6"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Reference.ListNeighborhoodsAsync("12a"));
            await Assert.ThrowsAsync<LunixiConfigurationException>(() => client.Reference.ListNeighborhoodsAsync("1234567890123"));
            Assert.Empty(requests);
            await client.Reference.ListProvincesAsync();
            await client.Reference.ListDistrictsAsync("06");
            await client.Reference.ListNeighborhoodsAsync("1234");
            Assert.Equal(new[]
            {
                "/api/v1/reference/tr-address/provinces",
                "/api/v1/reference/tr-address/districts?provinceCode=06",
                "/api/v1/reference/tr-address/neighborhoods?districtCode=1234",
            }, requests.Select(r => r.PathAndQuery));
        }
    }

    [Fact]
    public async Task DisplayInstructionIsReadFromTheActionOnly()
    {
        const string display = "{\"data\":{\"paymentId\":\"pay-1\",\"status\":\"PENDING\",\"action\":{\"id\":\"act-1\",\"type\":\"DISPLAY\",\"bankTransfer\":{\"bankCode\":\"0062\",\"bankName\":\"Synthetic Bank\",\"holderName\":\"Synthetic Merchant\",\"iban\":\"TR000000000000000000000000\",\"amountMinor\":\"10000\",\"currency\":\"TRY\",\"paymentCode\":\"LX12AB\",\"expiresAt\":\"2026-11-16T10:00:00Z\",\"otherBank\":false}}}}";
        var (client, _) = Create(_ => FakeHandler.Json(display, HttpStatusCode.Accepted));
        using (client)
        {
            var response = await client.Payments.DirectMethodPaymentAsync(Order().WithMethodFields(PaymentMethods.BankTransfer, null, new { bankCode = "0062" }), "direct:attempt-0002");
            var instruction = BankTransferInstruction.FromResponse(response);
            Assert.NotNull(instruction);
            Assert.Equal("0062", instruction.BankCode);
            Assert.Equal("Synthetic Bank", instruction.BankName);
            Assert.Equal("Synthetic Merchant", instruction.HolderName);
            Assert.Equal("TR000000000000000000000000", instruction.Iban);
            Assert.Equal("10000", instruction.AmountMinor);
            Assert.Equal("TRY", instruction.Currency);
            Assert.Equal("LX12AB", instruction.PaymentCode);
            Assert.Equal("2026-11-16T10:00:00Z", instruction.ExpiresAt);
            Assert.False(instruction.OtherBank);
            Assert.DoesNotContain("TR00", instruction.ToString());
        }
        using var otp = JsonDocument.Parse("{\"action\":{\"type\":\"OTP\",\"bankTransfer\":{\"iban\":\"TR00\"}}}");
        Assert.Null(BankTransferInstruction.FromData(otp.RootElement));
        using var none = JsonDocument.Parse("{\"status\":\"CAPTURED\"}");
        Assert.Null(BankTransferInstruction.FromData(none.RootElement));
    }
}
