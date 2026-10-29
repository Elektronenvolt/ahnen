using System.Text.Json.Serialization;

namespace Ahnen.Core.Models;

/// <summary>GEDCOM 7 SEX values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Sex
{
    U, // undetermined
    M,
    F,
    X  // other/intersex
}

/// <summary>GEDCOM 7 individual event/attribute tags supported by the app.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IndividualEventType
{
    BIRT, // birth
    DEAT, // death
    BURI, // burial
    CREM, // cremation
    BAPM, // baptism
    CHR,  // christening
    RESI, // residence
    OCCU, // occupation
    EDUC, // education
    EMIG, // emigration
    IMMI, // immigration
    NATU, // naturalization
    EVEN  // generic event, requires TypeDetail
}

/// <summary>GEDCOM 7 family event tags.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FamilyEventType
{
    MARR, // marriage
    DIV,  // divorce
    ANUL, // annulment
    DIVF, // divorce filed
    ENGA, // engagement
    MARB, // marriage banns
    MARC, // marriage contract
    MARL, // marriage license
    EVEN  // generic family event (e.g. separation), requires TypeDetail
}

/// <summary>GEDCOM 7 PEDI (pedigree) values for a child-to-family link.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Pedigree
{
    BIRTH,
    ADOPTED,
    FOSTER,
    SEALING,
    OTHER
}

/// <summary>GEDCOM 7 NAME.TYPE values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NameType
{
    BIRTH,
    MARRIED,
    AKA,
    MAIDEN,
    IMMIGRANT,
    PROFESSIONAL,
    OTHER
}
