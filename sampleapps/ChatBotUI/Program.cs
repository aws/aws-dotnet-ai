// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using ChatBotUI.Components;
using ChatBotUI.Models;
using ChatBotUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure AgentCore settings from appsettings.json
builder.Services.Configure<AgentCoreSettings>(builder.Configuration.GetSection("AgentCore"));

// Register services
builder.Services.AddSingleton<AgentCoreService>();
builder.Services.AddScoped<ChatSessionManager>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();