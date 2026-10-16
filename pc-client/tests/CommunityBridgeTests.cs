using System;
using System.Collections.Generic;
using LostHorizon.PC;
namespace LostHorizon.Tests {
 public static class CommunityBridgeTests {
  static void Check(bool value,string label){if(!value)throw new Exception(label);}
  public static void Run(string scenario){
   string gateway="http://127.0.0.1:41990",token=new string('A',43);
   Environment.SetEnvironmentVariable("LOST_HORIZON_GATEWAY",gateway);
   Environment.SetEnvironmentVariable("LOST_HORIZON_ACCOUNT","fixture-account");
   Environment.SetEnvironmentVariable("LOST_HORIZON_USERNAME","fixture-player");
   Environment.SetEnvironmentVariable("LOST_HORIZON_TOKEN",scenario=="bad-token"?"invalid":token);
   Environment.SetEnvironmentVariable("LOST_HORIZON_EXPIRES",(scenario=="expired"?DateTime.UtcNow.AddMinutes(-1):DateTime.UtcNow.AddHours(1)).Ticks.ToString());
   if(scenario=="missing")Environment.SetEnvironmentVariable("LOST_HORIZON_TOKEN",null);
   bool valid=scenario=="valid";
   Check(CommunitySessionBridge.Active==valid,"session validation");
   foreach(string key in new[]{"GATEWAY","ACCOUNT","USERNAME","TOKEN","EXPIRES"})Check(Environment.GetEnvironmentVariable("LOST_HORIZON_"+key)==null,"secret environment cleanup");
   var original=new Dictionary<string,string>{{"token","device-token"},{"account_id","device-id"},{"player","selected-character"}};
   foreach(string suffix in new[]{"/accounts","/sessions"}){
    var form=CommunitySessionBridge.InjectFields(gateway+suffix,original);
    Check(form["token"]==(valid?token:"device-token"),"authenticated token routing");
    Check(form["player"]=="selected-character","character preserved");
    if(valid){Check(form["account_id"]=="fixture-account"&&form["account_provider"]=="local","owner routing");}
   }
   Check(original["token"]=="device-token","caller form preserved");
   foreach(string url in new[]{"http://127.0.0.1:41991/accounts",gateway+"/accounts/",gateway+"/accounts?redirect=1",gateway+"/entry"})Check(object.ReferenceEquals(CommunitySessionBridge.InjectFields(url,original),original),"token isolation");
   if(valid)Check(CommunitySessionBridge.Servers.Count==1&&CommunitySessionBridge.Gateway==gateway,"single gateway");
   Console.WriteLine("PASS: community bridge "+scenario);
  }
 }
}
