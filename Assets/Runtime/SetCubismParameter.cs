using System.Linq;
using Live2D.Cubism.Core;
using Naninovel;
using Naninovel.Commands;
using UniRx.Async;
using UnityEngine;

[CommandAlias("param")]
public class SetCubismParameter : Command
{
    [ParameterAlias(NamelessParameterAlias), RequiredParameter]
    public NamedDecimalParameter ParamAndValue;
    [ParameterAlias("id"), RequiredParameter]
    public StringParameter ActorId;

    public override UniTask ExecuteAsync (CancellationToken cancellationToken = default)
    {
        var manager = Engine.GetService<ICharacterManager>();
        if (!manager.ActorExists(ActorId))
        {
            Debug.LogError($"Character `{ActorId}` is not added to scene.");
            return UniTask.CompletedTask;
        }

        var character = Engine.GetService<ICharacterManager>().GetActor(ActorId) as Live2DCharacter;
        if (character is null)
        {
            Debug.LogError($"Character `{ActorId}` is not of a Live2D implementation.");
            return UniTask.CompletedTask;
        }

        var model = character.GameObject.GetComponentInChildren<CubismModel>();
        if (model == null)
        {
            Debug.LogError($"Character `{ActorId}` is missing `{nameof(CubismModel)}` component.");
            return UniTask.CompletedTask;
        }

        var parameter = model.Parameters.FirstOrDefault(p => p.name.EqualsFastIgnoreCase(ParamAndValue.Name));
        if (parameter == null)
        {
            Debug.LogError($"Character `{ActorId}` is missing `{ParamAndValue.Name}` parameter.");
            return UniTask.CompletedTask;
        }

        parameter.Value = ParamAndValue.NamedValue;
        return UniTask.CompletedTask;
    }
}
