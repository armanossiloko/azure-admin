namespace AzureAdmin.API.Models;

public enum ReleasePrPhase
{
    DevToMaster = 0,
    MasterToProd = 1,
    /// <summary>Back-merge opened when prod is ahead of dev, before any release promotion.</summary>
    ProdToDev = 2
}
