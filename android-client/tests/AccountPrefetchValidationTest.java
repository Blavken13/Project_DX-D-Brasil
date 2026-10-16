package com.newdawn.launcher;
import android.os.Looper;
import org.json.JSONObject;
import org.json.JSONArray;
/** Runs in Android's actual org.json implementation using app_process. */
public final class AccountPrefetchValidationTest {
    private static int cases;
    private static JSONObject account() throws Exception {
        return new JSONObject().put("account_id", "fixture-owner").put("players", new JSONArray())
            .put("player_slot_count", 1).put("max_player_slot_count", 4);
    }
    private static JSONObject character() throws Exception {
        return new JSONObject().put("player_entity_id", "fixture-character").put("player_name", "Personagem de teste")
            .put("player_level", 1).put("disconnected_at", 0.0).put("deletes_at", JSONObject.NULL);
    }
    private static void check(JSONObject json, String owner, int expected) throws Exception {
        int count = AccountPreloader.validate(json.toString().getBytes("UTF-8"), owner);
        if (count != expected) throw new AssertionError("wrong character count"); cases++;
    }
    private static void reject(JSONObject json, String owner) throws Exception {
        try { AccountPreloader.validate(json.toString().getBytes("UTF-8"), owner); }
        catch (Exception expected) { cases++; return; }
        throw new AssertionError("invalid account accepted");
    }
    public static void main(String[] args) throws Exception {
        Looper.prepareMainLooper();
        check(account(), "fixture-owner", 0);
        check(account().put("players", new JSONArray().put(character())), "fixture-owner", 1);
        check(account().put("players", new JSONArray().put(character().put("deletes_at", 1791505726.0))), "fixture-owner", 1);
        reject(account(), "other-owner");
        reject(account().put("error", "unauthorized"), "fixture-owner");
        reject(account().put("players", "invalid"), "fixture-owner");
        reject(account().put("player_slot_count", -1), "fixture-owner");
        reject(account().put("max_player_slot_count", 0), "fixture-owner");
        reject(account().put("player_slot_count", true), "fixture-owner");
        reject(account().put("max_player_slot_count", 2147483648L), "fixture-owner");
        reject(account().put("players", new JSONArray().put(character()).put(character())), "fixture-owner");
        reject(account().put("players", new JSONArray().put(character().put("player_entity_id", ""))), "fixture-owner");
        reject(account().put("players", new JSONArray().put(character().put("player_level", -1))), "fixture-owner");
        reject(account().put("players", new JSONArray().put(character().put("disconnected_at", "bad"))), "fixture-owner");
        reject(account().put("players", new JSONArray().put(character().put("deletes_at", "bad"))), "fixture-owner");
        JSONArray many = new JSONArray(); for (int i=0;i<65;i++) many.put(character().put("player_entity_id", "c"+i));
        reject(account().put("players", many), "fixture-owner");
        String token="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        JSONObject saved=new JSONObject().put("token",token).put("expires_at",System.currentTimeMillis()+120000)
            .put("account_id","fixture-owner").put("username","fixture");
        if (!AccountPreloader.restore(saved.toString()).accessToken.equals(token)) throw new AssertionError("restore failed");
        cases++;
        saved.put("expires_at", 1);
        try { AccountPreloader.restore(saved.toString()); throw new AssertionError("expired session accepted"); }
        catch (NewDawnApi.ApiException expected) { cases++; }
        System.out.println("PASS: " + cases + " Android account validation cases (no server writes)");
    }
}
