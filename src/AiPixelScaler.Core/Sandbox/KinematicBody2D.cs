using AiPixelScaler.Core.Geometry;

namespace AiPixelScaler.Core.Sandbox;

public sealed class World2D
{
    public List<AxisAlignedBox> Obstacles { get; } = new();
    public double GroundY { get; set; } = 400;
}

public struct KinematicBody2D
{
    public double PosX;
    public double PosY;
    public double VelX;
    public double VelY;
}

public struct KinematicParams
{
    public double Acceleration;
    public double MaxSpeed;
    public double Friction;
}

public static class KinematicStep
{
    public static void Integrate(ref KinematicBody2D body, double inputX, double inputY, double deltaTime, KinematicParams p)
    {
        if (deltaTime <= 0)
            return;

        var ax = 0.0;
        var ay = 0.0;
        if (inputX != 0 || inputY != 0)
        {
            var len = Math.Sqrt(inputX * inputX + inputY * inputY);
            ax = inputX / len * p.Acceleration;
            ay = inputY / len * p.Acceleration;
            body.VelX += ax * deltaTime;
            body.VelY += ay * deltaTime;
        }
        else
        {
            var f = Math.Clamp(p.Friction, 0, 0.999999);
            body.VelX *= 1.0 - f;
            body.VelY *= 1.0 - f;
        }

        var sp = Math.Sqrt(body.VelX * body.VelX + body.VelY * body.VelY);
        if (sp > p.MaxSpeed && sp > 0)
        {
            var s = p.MaxSpeed / sp;
            body.VelX *= s;
            body.VelY *= s;
        }

        body.PosX += body.VelX * deltaTime;
        body.PosY += body.VelY * deltaTime;
    }
}
