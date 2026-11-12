using System.Text;

namespace Lunixi.Sdk.Tests;

public class ProductCategoryTests
{
    [Fact]
    public void UnsetCategoryIsOmittedAndSetCategoryIsSentAsProductCategory()
    {
        var plain = Encoding.UTF8.GetString(CanonicalJson.Serialize(new BasketItem("sku", "Product", "100")));
        Assert.DoesNotContain("productCategory", plain);

        var categorized = Encoding.UTF8.GetString(CanonicalJson.Serialize(new BasketItem("sku", "Product", "100") { ProductCategory = ProductCategories.MobilePhone }));
        Assert.Contains("\"productCategory\":\"MOBILE_PHONE\"", categorized);
    }

    [Fact]
    public void DictionaryFollowsTheDocumentedOrder()
    {
        Assert.Equal(40, ProductCategories.All.Count);
        Assert.Equal("MOBILE_PHONE", ProductCategories.All[0]);
        Assert.Equal("OTHER", ProductCategories.All[^1]);
    }
}
