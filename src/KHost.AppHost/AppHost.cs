var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.KHost_UserInterface>("khost-userinterface");

builder.Build().Run();
