#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Transformation;
using Vectorier.Gameplay.Trigger;
using Vectorier.Gameplay.Trigger.Events;
using Vectorier.Gameplay.User;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Level;

public class Location
{
    private List<QuadRunner> _culledQuads = new List<QuadRunner>();

    private List<QuadRunner> belongQuads = new List<QuadRunner>();

    public static Location Current => null;

    public List<ModelHuman> Models { get; }

    public string Music => Sets.Music;

    public List<UserData> UserData => Sets.UserData;

    public Sets Sets { get; private set; }

    public Grid.Grid Grid { get; } = new Grid.Grid();

    public TransformManager transformManager { get; }

    public Location(string path)
    {
        Models = new List<ModelHuman>();

        LoadXml(path);

        Grid.InitGrid(Sets.QuadsAll);
        transformManager = new TransformManager();
    }

    private void LoadXml(string xmlPath)
    {
        if (!xmlPath.EndsWith(".xml"))
        {
            xmlPath += ".xml";
        }
        // The level must be there and be XML: the error names the file (and the line where the XML breaks).
        Sets = new Sets(XmlUtils.ReadXmlDocument(xmlPath));

    }

    public void CreateModelHuman(UserData userData)
    {
        var model = new ModelHuman(userData);
        Models.Add(model);
        model.Start(GetSpawnByName(model.BirthSpawn));
    }

    public void Start()
    {
        Vector3 pos = new Vector3(0, 0, 0);
        if (Sets.Cameras.Count > 0)
        {
            pos = new Vector3(Sets.Cameras[0].LocalPosition, 0);
        }
        LocationCamera.Current.StartPosition(pos);
        LocationCamera.Current.UpdatePosition();
        TE_StartGame.ActivateThisEvent();
    }

    public void Render()
    {
        transformManager.Update();
        RenderModels();
    }

    public void RenderModels()
    {
        TriggerActionsRenderer.Current.Render();
        for (int i = 0; i < Models.Count; i++)
        {
            Models[i].ControllerAnimations.VelocityQuads();
            Models[i].Render();
            Collision(Models[i]);
        }
        RenderElements();
    }

    public void RenderElements()
    {
        foreach (var primitive in Sets.Primitives)
        {
            primitive.Render(Sets.Quads);
        }
        RunnerRender.RenderRunners();

    }

    public void Collision(ModelHuman modelHuman)
    {
        modelHuman.fixRectangle = true;
        _culledQuads.Clear();
        Grid.Collect(modelHuman.Rectangle, _culledQuads);
        belongQuads.Clear();
        var belongs = modelHuman.ControllerCollisions.ControllerPlatforms.Belongs;
        for (int i = belongs.Count - 2; i > 0; i--)
        {
            belongQuads.Add(belongs[i].Platform);
        }
        foreach (var quad in belongQuads)
        {
            _culledQuads.Remove(quad);
            modelHuman.ControllerCollisions.UpdateQuad(quad);
        }
        foreach (var quad1 in _culledQuads)
        {
            modelHuman.ControllerCollisions.UpdateQuad(quad1, modelHuman.Sign);
        }
        foreach (var item in Sets.Items)
        {
            item.Collision(modelHuman);
        }
        foreach (var primitive in Sets.Primitives)
        {
            if (!primitive.IsStrike)
            {
                modelHuman.ControllerCollisions.UpdatePrimitive(primitive);
            }
        }
        foreach (var model in Models)
        {
            modelHuman.ControllerCollisions.CheckModel(model);
        }
        modelHuman.fixRectangle = false;
    }

    public void Reload()
    {
        RunnerRender.Reset();
        TriggerActionsRenderer.Reset();
        ReloadLayers();
        ReloadModels();
        transformManager.Clear();
        foreach (var runner in Sets.AllRunners)
        {
            runner.Reset();
        }
    }

    private void ReloadLayers()
    {
        Grid.Reset();
    }

    private void ReloadModels()
    {
        foreach (var model in Models)
        {
            model.Reset();
            model.Start(GetSpawnByName(model.BirthSpawn));
        }
    }

    public SpawnRunner GetSpawnByName(string name)
    {
        foreach (var spawn in Sets.Spawns)
        {
            if (spawn.Name == name)
            {
                return spawn;
            }
        }
        return null;
    }

    public ModelHuman GetUserModel()
    {
        foreach (var model in Models)
        {
            if (model.IsSelf)
            {
                return model;
            }
        }
        return null;
    }

    public List<ModelHuman> GetAllBotModels()
    {
        List<ModelHuman> bots = new List<ModelHuman>();
        foreach (var model in Models)
        {
            if (model.IsBot)
            {
                bots.Add(model);
            }
        }
        return bots;
    }

    public ModelHuman GetModelByName(string name)
    {
        foreach (var model in Models)
        {
            if (model.Name == name)
            {
                return model;
            }
        }
        return null;
    }
}
