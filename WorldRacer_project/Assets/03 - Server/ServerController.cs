﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

public enum Playertype { Tobedetermined, Criminal, Cop };

public class Coordinate
{
    public double longitude;
    public double latitude;

    public Coordinate(double _latitude, double _longitude)
    {
        longitude = _longitude;
        latitude = _latitude;

    }
}

public class Playfield
{
    public List<Coordinate> points = new List<Coordinate>();

    public Coordinate copTargetPosition;
    public Coordinate criminalTargetPosition;

    public Playfield(JSONObject playfieldJson)
    {
        foreach (JSONObject pointJson in playfieldJson)
        {
            float longitude = pointJson.GetField("longitude").f;
            float latitude = pointJson.GetField("latitude").f;

            Coordinate newPoint = new Coordinate(latitude, longitude);

            points.Add(newPoint);
        }
    }

    public Playfield()
    {

    }
}


public class Player
{
    public string ip;
    public string name;
    public bool isHost;
    public bool isReady;

    public Coordinate position;

    public Playertype playertype;
}


public class Game
{
    public bool started;
    public int time;
    public Playfield playfield;
    public List<Player> players;
    public bool caught;
}


public class ServerController : MonoBehaviour
{
    [NonSerialized]
    public string serverAddress;

    private Dictionary<string, string> settings = new Dictionary<string, string>();
    
    [NonSerialized]
    public string roomPin;
    [NonSerialized]
    public string playerIp;
    [NonSerialized]
    public string playerName;
    [NonSerialized]
    public bool isHost;
    [NonSerialized]
    public Playertype playertype = Playertype.Tobedetermined;
    [NonSerialized]
    public Coordinate position = new Coordinate(0, 0);
    [NonSerialized]
    public Vector2 targetPosition;
    [NonSerialized]
    public float maxTargetDistance;
    [NonSerialized]
    public bool isAtStart;

    [NonSerialized]
    public float currentTime;

    public Playfield editingPlayfield = new Playfield
    {
        points = new List<Coordinate>()
    };
    
    

    public UIManager uiManager;

    public UIScreenManager uiScreenRoom;
    public UIScreenManager uiScreenHome;
    public UIScreenManager uiScreenGame;

    public UIScreenManager uiScreenCopsWinCriminal;
    public UIScreenManager uiScreenCriminalWinsCriminal;
    public UIScreenManager uiScreenCopsWinCops;
    public UIScreenManager uiScreenCriminalWinsCops;
    public UIScreenManager uiScreenStopped;


    public Game game;

    
    [System.NonSerialized]
    public UnityEvent updateRoomData = new UnityEvent();
    [System.NonSerialized]
    public UnityEvent updateGameData = new UnityEvent();
    [System.NonSerialized]
    public UnityEvent getInitialGameData = new UnityEvent();

    [Header("Server update timing")]
    public float updateRoomDataDelay;
    private bool continueUpdatingRoomData;

    public float updateGameDataDelay;
    private bool continueUpdatingGameData;

    public float startGameDelay;

    private void Update()
    {
        if (game != null)
        {
            if (game.started)
            {
                currentTime -= Time.deltaTime;

                if (currentTime <= 0)
                {
                    if (playertype == Playertype.Cop)
                    {
                        uiManager.NextScreen(uiScreenCriminalWinsCops);
                    } else if (playertype == Playertype.Criminal)
                    {
                        uiManager.NextScreen(uiScreenCriminalWinsCriminal);
                    }
                    StopUpdatingGameData();

                }
            }
        }
        
    }

    public void UpdateFields(JSONObject fieldsJson)
    {
        for (int i = 0; i < fieldsJson.list.Count; i++)
        {
            string key = fieldsJson.keys[i];
            string value = fieldsJson.list[i].str;
            settings[key] = value;
            
        }
    }


    public string GetField(string key)
    {
        return settings.ContainsKey(key) ? settings[key] : "";
    }


    public void TestConnection()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "test_connection");

        StartCoroutine(SendRequest(sendObject, false, TestConnectionCallback));
    }


    private void TestConnectionCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            uiManager.ShowPopup("Server address is valid", uiManager.popupDuration);

        }
    }


    public void CreateGame(int time, Playfield playfield)
    {
        if (playfield.points.Count == 0)
        {
            uiManager.ShowPopup("Please define the playfield first.", uiManager.popupDuration);
            return;
        }

        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "create_game");
        sendObject.AddField("time", time);

        JSONObject playfieldObject = new JSONObject();
        List<Coordinate> points = playfield.points;
        foreach (Coordinate point in points)
        {
            JSONObject pointObject = new JSONObject();
            pointObject.AddField("longitude", point.longitude);
            pointObject.AddField("latitude", point.latitude);

            playfieldObject.Add(pointObject);
        }
        sendObject.AddField("playfield", playfieldObject);

        sendObject.AddField("name", GetField("name"));

        StartCoroutine(SendRequest(sendObject, true, CreateGameCallback));
    }


    private void CreateGameCallback(JSONObject incomingJson)
    {
        roomPin = incomingJson.GetField("room_pin").str;
        isHost = true;

        string status = incomingJson.GetField("status").str;

        // Create game object with right variables
        PopulateRoom(incomingJson);

        if (status == "success")
        {
            playerIp = incomingJson.GetField("ip").str;
            playerName = incomingJson.GetField("name").str;
            uiManager.NextScreen(uiScreenRoom);


            StartUpdatingRoomData();

        } else if (status == "failed")
        {
            Debug.Log("Room not created");
            uiManager.ShowPopup("Couldn't create game.", uiManager.popupDuration);
        }
    }


    public void JoinGame(string newRoomPin)
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "join_game");
        sendObject.AddField("room_pin", newRoomPin);

        sendObject.AddField("name", GetField("name"));

        StartCoroutine(SendRequest(sendObject, true, JoinGameCallback));
    }


    private void JoinGameCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            Debug.Log("Room found");
            playerIp = incomingJson.GetField("ip").str;
            playerName = incomingJson.GetField("name").str;
            roomPin = incomingJson.GetField("room_pin").str;
            isHost = false;

            // Create game object with right variables
            PopulateRoom(incomingJson);

            StartUpdatingRoomData();

            uiManager.NextScreen(uiScreenRoom);      
        }
        else if (status == "busy")
        {
            uiManager.ShowPopup("This room has already started", uiManager.popupDuration);
        }
        else if (status == "failed")
        {
            uiManager.ShowPopup("Room not found", uiManager.popupDuration);
        }
    }


    public void KickPlayer(string kickIp, string kickName)
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "kick_player");
        sendObject.AddField("room_pin", roomPin);
        sendObject.AddField("kick_ip", kickIp);
        sendObject.AddField("kick_name", kickName);

        StartCoroutine(SendRequest(sendObject, true, KickPlayerCallback));
    }


    private void KickPlayerCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            Debug.Log("Kicked player");
            uiManager.ShowPopup(string.Format("Kicked {0} from room", incomingJson.GetField("kick_name").str), uiManager.popupDuration);

            UpdateRoomData();
        }
        else if (status == "failed")
        {
            Debug.Log("Couldn't kick player");
            uiManager.ShowPopup("Couldn't kick player", uiManager.popupDuration);
        }
    }


    public void LeaveGame()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "leave_game");
        sendObject.AddField("room_pin", roomPin);
        sendObject.AddField("name", GetField("name"));
        sendObject.AddField("is_host", isHost);

        StartCoroutine(SendRequest(sendObject, true, LeaveGameCallback));
    }


    private void LeaveGameCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            Debug.Log("Left game");
            uiManager.PreviousScreen(uiScreenHome);

            StopUpdatingRoomData();

        }
        else if (status == "room_starting")
        {
            uiManager.ShowPopup("You can't leave now because the game is starting", uiManager.popupDuration);
        }
        else if (status == "failed")
        {
            Debug.Log("Failed to leave game");

            uiManager.PreviousScreen(uiScreenHome);

            uiManager.ShowPopup("Room doesn't exist anymore", uiManager.popupDuration);

            StopUpdatingRoomData();

        }
    }


    private void PopulateRoom(JSONObject incomingJson)
    {

        game = new Game
        {
            time = (int)incomingJson.GetField("time").i,
            playfield = new Playfield(incomingJson.GetField("playfield")),
            started = false,
            players = new List<Player>()
        };

        JSONObject playerlistJson = incomingJson.GetField("playerlist");
        foreach (JSONObject playerJson in playerlistJson)
        {
            Player newPlayer = new Player
            {
                name = playerJson.GetField("name").str,
                ip = playerJson.GetField("ip").str,
                isHost = playerJson.GetField("is_host").b,
                playertype = Playertype.Tobedetermined
            };
            game.players.Add(newPlayer);
        }
    }

    private void SetPlayerTypes(JSONObject playerlistJson)
    {

        for (int i=0; i<game.players.Count; i++)
        {
            Player player = game.players[i];

            int raw_playertype = (int)playerlistJson[i].GetField("playertype").i;

            if (raw_playertype == 1)
            {
                player.playertype = Playertype.Cop;
            }
            else if (raw_playertype == 2)
            {
                player.playertype = Playertype.Criminal;
            }
        }
    }


    // Update room data functions
    public void UpdateRoomData()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "update_room_data");
        sendObject.AddField("room_pin", roomPin);
        sendObject.AddField("name", playerName);

        StartCoroutine(SendRequest(sendObject, false, UpdateRoomDataCallback));
    }
    public void StartUpdatingRoomData()
    {
        continueUpdatingRoomData = true;
        StartCoroutine(CycleUpdateRoomData());
    }
    public void StopUpdatingRoomData()
    {
        continueUpdatingRoomData = false;
    }
    private void UpdateRoomDataCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {

            // Create game object with right variables
            JSONObject playerlistJson = incomingJson.GetField("playerlist");
            game.players = new List<Player>();
            bool kicked = true;
            foreach (JSONObject playerJson in playerlistJson)
            {
                Player newPlayer = new Player
                {
                    name = playerJson.GetField("name").str,
                    ip = playerJson.GetField("ip").str,
                    isHost = playerJson.GetField("is_host").b
                };
                if (newPlayer.name == playerName && newPlayer.ip == playerIp)
                {
                    kicked = false;

                    if (newPlayer.isHost)
                    {
                        isHost = true;
                    }
                }
                game.players.Add(newPlayer);
            }

            if (kicked)
            {
                StopUpdatingRoomData();

                uiManager.ShowPopup("You have been kicked from this room", uiManager.popupDuration);
                uiManager.PreviousScreen(uiScreenHome);
            }

            if (incomingJson.GetField("starting").b == true)
            {
                float delay = incomingJson.GetField("delay").f;
                Debug.Log("Time before start: " + delay.ToString());
                uiManager.ShowPopup(string.Format("Game will start in {0} seconds", delay), uiManager.popupDuration);

                uiManager.DismissBottomOverlay();
                uiManager.DeactivateScreen(uiManager.currentScreen);

                int playertypeInt = (int)incomingJson.GetField("playertype").i;

                if (playertypeInt == 1)
                {
                    playertype = Playertype.Cop;
                }
                else if (playertypeInt == 2)
                {
                    playertype = Playertype.Criminal;
                }

                SetPlayerTypes(playerlistJson);

                StartCoroutine(StartGameAfter(delay));

                StopUpdatingRoomData();
            }

            updateRoomData.Invoke();

        }
        else if (status == "failed")
        {
            Debug.Log("Room deleted");
            uiManager.ShowPopup("This room doesn't exist anymore", uiManager.popupDuration);
            uiManager.PreviousScreen(uiScreenHome);

            StopUpdatingRoomData();
        }
    }
    private IEnumerator CycleUpdateRoomData()
    {
        while (continueUpdatingRoomData)
        {
            UpdateRoomData();
            yield return new WaitForSeconds(updateRoomDataDelay);
        }
    }


    public void GetInitialGameData()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "get_initial_game_data");
        sendObject.AddField("room_pin", roomPin);

        StartCoroutine(SendRequest(sendObject, false, GetInitialGameDataCallback));
    }


    public void GetInitialGameDataCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            JSONObject cop_target_position = incomingJson.GetField("cop_target_position");
            game.playfield.copTargetPosition = new Coordinate(cop_target_position.GetField("latitude").f, cop_target_position.GetField("longitude").f);

            JSONObject criminal_target_position = incomingJson.GetField("criminal_target_position");
            game.playfield.criminalTargetPosition = new Coordinate(criminal_target_position.GetField("latitude").f, criminal_target_position.GetField("longitude").f);

            getInitialGameData.Invoke();
        }
        else if (status == "failed")
        {
            Debug.Log("Room not found");
        }
    }

    // Update game data functions
    public void UpdateGameData()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "update_game_data");
        sendObject.AddField("room_pin", roomPin);
        sendObject.AddField("name", playerName);
        sendObject.AddField("caught", game.caught);


        //PlayerScript playerScript = FindObjectOfType<PlayerScript>();
        //position = playerScript.position;
        /*}
        else
        {
            position = new Coordinate(0, 0);
            uiManager.ShowPopup("Couldn't send coordinates", uiManager.popupDuration);
        }*/


        // Send current location of player
        JSONObject positionJson = new JSONObject();
        positionJson.AddField("longitude", position.longitude);
        positionJson.AddField("latitude", position.latitude);
        sendObject.AddField("position", positionJson);

        StartCoroutine(SendRequest(sendObject, false, UpdateGameDataCallback));
    }
    public void StartUpdatingGameData()
    {
        continueUpdatingGameData = true;
        GetInitialGameData();
        StartCoroutine(CycleUpdateGameData());
    }
    public void StopUpdatingGameData()
    {
        continueUpdatingGameData = false;
        Destroy(FindObjectOfType<GameController>().game);
        game = null;
    }
    private void UpdateGameDataCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {

            if (incomingJson.GetField("stopped").b)
            {
                uiManager.NextScreen(uiScreenStopped);
                StopUpdatingGameData();
            }

            // Check if all players are ready.
            if (incomingJson.GetField("game_started").b)
            {
                if (!game.started)
                {
                    // Game has started
                    // Get all player types
                    game.started = true;

                    currentTime = game.time * 60;
                } else
                {
                    currentTime = incomingJson.GetField("time_left").f;
                }

                
            }

            game.caught = incomingJson.GetField("caught").b;

            if (game.caught)
            {
                if (playertype == Playertype.Cop)
                {
                    uiManager.NextScreen(uiScreenCopsWinCops);
                }
                else if (playertype == Playertype.Criminal)
                {
                    uiManager.NextScreen(uiScreenCopsWinCriminal);
                }
                StopUpdatingGameData();
            }

            JSONObject playerlistJson = incomingJson.GetField("playerlist");

            int length = playerlistJson.Count;

            for (int i=0; i<length; i++)
            {
                Player player = game.players[i];
                JSONObject playerJson = playerlistJson[i];

                JSONObject positionJson = playerJson.GetField("position");
                Coordinate newCoordinate = new Coordinate(positionJson.GetField("latitude").f, positionJson.GetField("longitude").f);
                player.position = newCoordinate;
            }

            if (incomingJson.GetField("ready").b)
            {
                isAtStart = true;
            } else
            {
                isAtStart = false;
            }

            updateGameData.Invoke();
        }
        else if (status == "failed")
        {
            Debug.Log("Room deleted");
            uiManager.ShowPopup("This room doesn't exist anymore", uiManager.popupDuration);
            uiManager.PreviousScreen(uiScreenHome);

            StopUpdatingGameData();
        }
    }
    private IEnumerator CycleUpdateGameData()
    {
        while (continueUpdatingGameData)
        {
            UpdateGameData();
            yield return new WaitForSeconds(updateGameDataDelay);
        }
    }


    public void RequestStartGame()
    {
        if (game.players.Count >= 2)
        {

            JSONObject sendObject = new JSONObject();
            sendObject.AddField("action", "request_start_game");
            sendObject.AddField("room_pin", roomPin);
            sendObject.AddField("delay", startGameDelay);
            sendObject.AddField("name", playerName);

            StartCoroutine(SendRequest(sendObject, true, RequestStartGameCallback));
        } else
        {
            uiManager.ShowPopup("There are not enough players in this room.", uiManager.popupDuration);
        }

    }


    private void RequestStartGameCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            uiManager.ShowPopup(string.Format("Game will start in {0} seconds", startGameDelay), uiManager.popupDuration);

            StartCoroutine(StartGameAfter(startGameDelay));

            int raw_playertype = (int)incomingJson.GetField("playertype").i;

            if (raw_playertype == 1)
            {
                playertype = Playertype.Cop;
            }
            else if (raw_playertype == 2)
            {
                playertype = Playertype.Criminal;
            }

            SetPlayerTypes(incomingJson.GetField("playerlist"));


            StopUpdatingRoomData();
        }
        else if (status == "not_enough_players")
        {
            uiManager.ShowPopup("There are not enough players in this room.", uiManager.popupDuration);
        }
        else if (status == "already_starting")
        {
            uiManager.ShowPopup("The game is already starting.", uiManager.popupDuration);
        }
        else if (status == "failed")
        {
            Debug.Log("Room deleted");
            uiManager.ShowPopup("This room doesn't exist anymore", uiManager.popupDuration);
            uiManager.PreviousScreen(uiScreenHome);

            StopUpdatingRoomData();
        }
    }


    IEnumerator StartGameAfter(float time)
    {
        uiManager.DeactivateScreen(uiManager.currentScreen);
        uiManager.DismissBottomOverlay();
        yield return new WaitForSeconds(time);



        StopUpdatingRoomData();

        StartUpdatingGameData();
        uiManager.NextScreen(uiScreenGame);
    }

    public void ExitGame()
    {
        JSONObject sendObject = new JSONObject();
        sendObject.AddField("action", "exit_game");
        sendObject.AddField("room_pin", roomPin);
        sendObject.AddField("name", playerName);

        StartCoroutine(SendRequest(sendObject, false, ExitGameCallback));
    }

    private void ExitGameCallback(JSONObject incomingJson)
    {
        string status = incomingJson.GetField("status").str;

        if (status == "success")
        {
            uiManager.PreviousScreen(uiScreenHome);
            StopUpdatingGameData();
        }
        else if (status == "failed")
        {
            uiManager.ShowPopup("This room doesn't exist anymore", uiManager.popupDuration);
            uiManager.PreviousScreen(uiScreenHome);
            StopUpdatingGameData();
        }
    }


    private IEnumerator SendRequest(JSONObject outgoingJson, bool disableScreen, Action<JSONObject> callback = null)
    {
        if (disableScreen)
        {
            uiManager.DeactivateScreen(uiManager.currentScreen);

            if (uiManager.currentOverlayScreen != null)
            {
                uiManager.DeactivateScreen(uiManager.currentOverlayScreen);
            }
        }
        

        Debug.Log(outgoingJson);

        string jsonString = outgoingJson.ToString();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(jsonString);

        string address = GetField("serverAddress");

        using (UnityWebRequest webRequest = UnityWebRequest.Put("http://"+address, bytes))
        {
            webRequest.method = "POST";
            webRequest.SetRequestHeader("Content-Type", "application/json");

            yield return webRequest.SendWebRequest();

            if (disableScreen)
            {
                uiManager.ActivateScreen(uiManager.currentScreen);
                if (uiManager.currentOverlayScreen != null)
                {
                    uiManager.ActivateScreen(uiManager.currentOverlayScreen);
                }
            }

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Debug.Log(webRequest.error);
                uiManager.ShowPopup("Couldn't connect to server", uiManager.popupDuration);
            }
            else
            {
                byte[] answer = webRequest.downloadHandler.data;
                string answerString = System.Text.Encoding.UTF8.GetString(answer);

                JSONObject incomingJson = new JSONObject(answerString);
                Debug.Log(incomingJson);
                callback?.Invoke(incomingJson);

                

            }
        }
    }
}
