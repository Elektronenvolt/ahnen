using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("", "", "?", "?")]
    [InlineData("anna", "", "anna", "A")]
    [InlineData("", "smith", "smith", "S")]
    [InlineData("anna", "smith", "anna smith", "AS")]
    public void Display_name_and_initials_use_the_primary_name(string given, string surname,
        string display, string initials)
    {
        var person = new Person { Names = [new() { Given = given, Surname = surname }, new() { Given = "Other" }] };
        Assert.Same(person.Names[0], person.PrimaryName);
        Assert.Equal(display, person.DisplayName);
        Assert.Equal(initials, UiHelpers.Initials(person));
    }

    [Fact]
    public void Empty_name_list_has_safe_display_defaults()
    {
        var person = new Person { Names = [] };
        Assert.Equal("?", person.DisplayName);
        Assert.Equal("?", UiHelpers.Initials(person));
    }

    [Fact]
    public void Full_name_preserves_components_and_omits_blank_parts()
    {
        var name = new PersonName { Prefix = "Dr.", Given = "Anna", Surname = "Smith", Suffix = "Jr.", Nickname = "Annie" };
        Assert.Equal("Dr. Anna Smith Jr.", name.Full);
        name.Prefix = " ";
        name.Suffix = null;
        Assert.Equal("Anna Smith", name.Full);
    }

    [Fact]
    public void Address_summary_combines_lines_postal_city_and_country()
    {
        var address = new Address();
        Assert.True(address.IsEmpty);
        Assert.Equal("", address.Summary);
        address.City = " ";
        Assert.True(address.IsEmpty);
        address.Lines.Add("Main Street 1");
        address.PostalCode = "12345";
        address.City = "Town";
        address.Country = "Country";
        Assert.False(address.IsEmpty);
        Assert.Equal("Main Street 1, 12345 Town, Country", address.Summary);
    }

    [Theory]
    [InlineData(null, false, null, "")]
    [InlineData("ABT 1900", false, null, "1900")]
    [InlineData("1900", true, "BEF 1980", "1900\u20131980")]
    [InlineData("1900", true, null, "1900\u2013?")]
    [InlineData(null, true, "1980", "?\u20131980")]
    public void Life_years_reflect_known_dates_and_death(string? birth, bool deceased, string? death, string expected)
    {
        var person = new Person();
        if (birth is not null) person.GetOrAddEvent(IndividualEventType.BIRT).Date = birth;
        if (deceased) person.GetOrAddEvent(IndividualEventType.DEAT).Date = death;
        Assert.Equal(expected, person.LifeYears);
        Assert.Equal(deceased, person.IsDeceased);
    }

    [Fact]
    public void GetOrAddEvent_reuses_existing_event_without_losing_details()
    {
        var person = new Person();
        var birth = person.GetOrAddEvent(IndividualEventType.BIRT);
        birth.Date = "1900";
        Assert.Same(birth, person.GetOrAddEvent(IndividualEventType.BIRT));
        Assert.Equal("1900", Assert.Single(person.Events).Date);
    }

    [Fact]
    public void Family_selects_other_partner_and_latest_dated_event()
    {
        var marriage = new FamilyEvent { Type = FamilyEventType.MARR, Date = "1950" };
        var divorce = new FamilyEvent { Type = FamilyEventType.DIV, Date = "1980" };
        var family = new Family
        {
            PartnerIds = ["I1", "I2"],
            Events = [divorce, marriage, new() { Type = FamilyEventType.EVEN, Date = "2000" }]
        };
        Assert.Equal("I2", family.OtherPartner("I1"));
        Assert.Same(marriage, family.Marriage);
        Assert.Same(divorce, family.LatestStatusEvent);
        family.PartnerIds.Remove("I2");
        Assert.Null(family.OtherPartner("I1"));
    }
}
