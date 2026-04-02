using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Collider、Rigidbody、Joint の追加・設定・検査ツール。
    /// VRChat PhysBone コライダー設定にも使用。
    /// </summary>
    public static class PhysicsTools
    {
        // =================================================================
        // Collider — Add
        // =================================================================

        [AgentTool("Add a BoxCollider to a GameObject. center and size are 'x,y,z' format. Defaults: center='0,0,0', size='1,1,1'.")]
        public static string AddBoxCollider(string goName, string center = "0,0,0", string size = "1,1,1", bool isTrigger = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var c = Undo.AddComponent<BoxCollider>(go);
            var ctr = ParseVector3(center);
            var sz = ParseVector3(size);
            if (ctr.HasValue) c.center = ctr.Value;
            if (sz.HasValue) c.size = sz.Value;
            c.isTrigger = isTrigger;

            return $"Success: Added BoxCollider to '{goName}' (center={c.center}, size={c.size}, trigger={isTrigger}).";
        }

        [AgentTool("Add a SphereCollider to a GameObject. center is 'x,y,z' format.")]
        public static string AddSphereCollider(string goName, float radius = 0.5f, string center = "0,0,0", bool isTrigger = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var c = Undo.AddComponent<SphereCollider>(go);
            var ctr = ParseVector3(center);
            if (ctr.HasValue) c.center = ctr.Value;
            c.radius = radius;
            c.isTrigger = isTrigger;

            return $"Success: Added SphereCollider to '{goName}' (radius={radius}, center={c.center}, trigger={isTrigger}).";
        }

        [AgentTool("Add a CapsuleCollider to a GameObject. direction: 0=X, 1=Y(default), 2=Z.")]
        public static string AddCapsuleCollider(string goName, float radius = 0.5f, float height = 2f, int direction = 1, string center = "0,0,0", bool isTrigger = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var c = Undo.AddComponent<CapsuleCollider>(go);
            var ctr = ParseVector3(center);
            if (ctr.HasValue) c.center = ctr.Value;
            c.radius = radius;
            c.height = height;
            c.direction = Mathf.Clamp(direction, 0, 2);
            c.isTrigger = isTrigger;

            string[] dirs = { "X", "Y", "Z" };
            return $"Success: Added CapsuleCollider to '{goName}' (radius={radius}, height={height}, dir={dirs[c.direction]}, trigger={isTrigger}).";
        }

        [AgentTool("Add a MeshCollider to a GameObject. convex must be true for triggers and rigidbodies.")]
        public static string AddMeshCollider(string goName, bool convex = false, bool isTrigger = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var c = Undo.AddComponent<MeshCollider>(go);
            c.convex = convex;
            c.isTrigger = isTrigger && convex;

            if (isTrigger && !convex)
                return $"Warning: Added MeshCollider to '{goName}' but isTrigger requires convex=true. Set convex=false, trigger=false.";

            return $"Success: Added MeshCollider to '{goName}' (convex={c.convex}, trigger={c.isTrigger}).";
        }

        // =================================================================
        // Collider — Inspect / Configure / Remove
        // =================================================================

        [AgentTool("List all Collider components on a GameObject (and optionally its children). Shows type, center, size/radius, trigger state.")]
        public static string ListColliders(string goName, bool includeChildren = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            Collider[] colliders = includeChildren
                ? go.GetComponentsInChildren<Collider>(true)
                : go.GetComponents<Collider>();

            if (colliders.Length == 0)
                return $"No colliders found on '{goName}'{(includeChildren ? " (including children)" : "")}.";

            var sb = new StringBuilder();
            sb.AppendLine($"Colliders on '{goName}'{(includeChildren ? " (including children)" : "")} ({colliders.Length}):");

            foreach (var c in colliders)
            {
                string goPath = includeChildren ? GetRelativePath(c.transform, go.transform) : "";
                string prefix = string.IsNullOrEmpty(goPath) ? "" : $"[{goPath}] ";

                if (c is BoxCollider box)
                    sb.AppendLine($"  {prefix}BoxCollider: center={box.center}, size={box.size}, trigger={box.isTrigger}");
                else if (c is SphereCollider sphere)
                    sb.AppendLine($"  {prefix}SphereCollider: center={sphere.center}, radius={sphere.radius}, trigger={sphere.isTrigger}");
                else if (c is CapsuleCollider cap)
                {
                    string[] dirs = { "X", "Y", "Z" };
                    sb.AppendLine($"  {prefix}CapsuleCollider: center={cap.center}, radius={cap.radius}, height={cap.height}, dir={dirs[cap.direction]}, trigger={cap.isTrigger}");
                }
                else if (c is MeshCollider mesh)
                    sb.AppendLine($"  {prefix}MeshCollider: convex={mesh.convex}, trigger={mesh.isTrigger}");
                else
                    sb.AppendLine($"  {prefix}{c.GetType().Name}: trigger={c.isTrigger}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Configure an existing collider on a GameObject. colliderIndex selects which collider (0-based) if multiple exist. Use -1 for unchanged values.")]
        public static string ConfigureCollider(string goName, int colliderIndex = 0, bool isTrigger = false, string center = "", string size = "", float radius = -1f, float height = -1f, int direction = -1)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var colliders = go.GetComponents<Collider>();
            if (colliders.Length == 0)
                return $"Error: No colliders on '{goName}'.";
            if (colliderIndex < 0 || colliderIndex >= colliders.Length)
                return $"Error: Collider index {colliderIndex} out of range (0-{colliders.Length - 1}).";

            var c = colliders[colliderIndex];
            Undo.RecordObject(c, "Configure Collider via Agent");

            c.isTrigger = isTrigger;
            var ctr = ParseVector3(center);

            if (c is BoxCollider box)
            {
                if (ctr.HasValue) box.center = ctr.Value;
                var sz = ParseVector3(size);
                if (sz.HasValue) box.size = sz.Value;
            }
            else if (c is SphereCollider sphere)
            {
                if (ctr.HasValue) sphere.center = ctr.Value;
                if (radius >= 0) sphere.radius = radius;
            }
            else if (c is CapsuleCollider cap)
            {
                if (ctr.HasValue) cap.center = ctr.Value;
                if (radius >= 0) cap.radius = radius;
                if (height >= 0) cap.height = height;
                if (direction >= 0 && direction <= 2) cap.direction = direction;
            }
            else if (c is MeshCollider mesh)
            {
                // size/center not applicable
            }

            EditorUtility.SetDirty(c);
            return $"Success: Configured {c.GetType().Name}[{colliderIndex}] on '{goName}'.";
        }

        [AgentTool("Remove a collider from a GameObject by index. Requires confirmation.")]
        public static string RemoveCollider(string goName, int colliderIndex = 0)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var colliders = go.GetComponents<Collider>();
            if (colliders.Length == 0)
                return $"Error: No colliders on '{goName}'.";
            if (colliderIndex < 0 || colliderIndex >= colliders.Length)
                return $"Error: Collider index {colliderIndex} out of range (0-{colliders.Length - 1}).";

            if (!AgentSettings.RequestConfirmation("Colliderを削除",
                $"'{goName}' から {colliders[colliderIndex].GetType().Name}[{colliderIndex}] を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(colliders[colliderIndex]);
            return $"Success: Removed collider[{colliderIndex}] from '{goName}'.";
        }

        // =================================================================
        // Rigidbody
        // =================================================================

        [AgentTool("Add a Rigidbody to a GameObject. isKinematic=true is common for animated objects.")]
        public static string AddRigidbody(string goName, float mass = 1f, bool useGravity = true, bool isKinematic = false)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            if (go.GetComponent<Rigidbody>() != null)
                return $"Error: '{goName}' already has a Rigidbody.";

            var rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = mass;
            rb.useGravity = useGravity;
            rb.isKinematic = isKinematic;

            return $"Success: Added Rigidbody to '{goName}' (mass={mass}, gravity={useGravity}, kinematic={isKinematic}).";
        }

        [AgentTool("Configure an existing Rigidbody. Use -1 for unchanged float values.")]
        public static string ConfigureRigidbody(string goName, float mass = -1f, float drag = -1f, float angularDrag = -1f, int useGravity = -1, int isKinematic = -1)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) return $"Error: No Rigidbody on '{goName}'.";

            Undo.RecordObject(rb, "Configure Rigidbody via Agent");

            if (mass >= 0) rb.mass = mass;
            if (drag >= 0) rb.drag = drag;
            if (angularDrag >= 0) rb.angularDrag = angularDrag;
            if (useGravity >= 0) rb.useGravity = useGravity != 0;
            if (isKinematic >= 0) rb.isKinematic = isKinematic != 0;

            EditorUtility.SetDirty(rb);
            return $"Success: Configured Rigidbody on '{goName}' (mass={rb.mass}, drag={rb.drag}, angDrag={rb.angularDrag}, gravity={rb.useGravity}, kinematic={rb.isKinematic}).";
        }

        [AgentTool("Inspect a Rigidbody and show all settings.")]
        public static string InspectRigidbody(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) return $"Error: No Rigidbody on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Rigidbody on '{goName}':");
            sb.AppendLine($"  Mass: {rb.mass}");
            sb.AppendLine($"  Drag: {rb.drag}");
            sb.AppendLine($"  Angular Drag: {rb.angularDrag}");
            sb.AppendLine($"  Use Gravity: {rb.useGravity}");
            sb.AppendLine($"  Is Kinematic: {rb.isKinematic}");
            sb.AppendLine($"  Interpolation: {rb.interpolation}");
            sb.AppendLine($"  Collision Detection: {rb.collisionDetectionMode}");
            sb.AppendLine($"  Constraints: {rb.constraints}");

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Joints
        // =================================================================

        [AgentTool("Add a Joint to a GameObject. type: Fixed, Hinge, Spring, Character, Configurable. connectedBodyName is optional.")]
        public static string AddJoint(string goName, string type, string connectedBodyName = "")
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            Type jointType;
            switch (type.ToLower())
            {
                case "fixed": jointType = typeof(FixedJoint); break;
                case "hinge": jointType = typeof(HingeJoint); break;
                case "spring": jointType = typeof(SpringJoint); break;
                case "character": jointType = typeof(CharacterJoint); break;
                case "configurable": jointType = typeof(ConfigurableJoint); break;
                default: return $"Error: Unknown joint type '{type}'. Valid: Fixed, Hinge, Spring, Character, Configurable.";
            }

            var joint = Undo.AddComponent(go, jointType) as Joint;

            if (!string.IsNullOrEmpty(connectedBodyName))
            {
                var connectedGo = FindGO(connectedBodyName);
                if (connectedGo != null)
                {
                    var rb = connectedGo.GetComponent<Rigidbody>();
                    if (rb != null) joint.connectedBody = rb;
                    else return $"Warning: Added {type}Joint to '{goName}' but '{connectedBodyName}' has no Rigidbody.";
                }
                else
                    return $"Warning: Added {type}Joint to '{goName}' but connected body '{connectedBodyName}' not found.";
            }

            return $"Success: Added {type}Joint to '{goName}'.";
        }

        [AgentTool("List all Joint components on a GameObject. Shows type, connected body, break forces.")]
        public static string ListJoints(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<Joint>();
            if (joints.Length == 0) return $"No joints on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Joints on '{goName}' ({joints.Length}):");
            foreach (var j in joints)
            {
                string connected = j.connectedBody != null ? j.connectedBody.name : "none";
                sb.AppendLine($"  {j.GetType().Name}: connected={connected}, breakForce={j.breakForce}, breakTorque={j.breakTorque}");
            }
            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Joint — Detailed Configuration
        // =================================================================

        [AgentTool(@"Configure common Joint properties (works for any joint type). anchor/connectedAnchor format: 'x,y,z'. Use -1 for unchanged float values.
jointIndex: selects which joint if multiple exist (0-based).")]
        public static string ConfigureJointBase(string goName, int jointIndex = 0, float breakForce = -1f, float breakTorque = -1f,
            string anchor = "", string connectedAnchor = "", int enableCollision = -1, int enablePreprocessing = -1)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<Joint>();
            if (joints.Length == 0) return $"Error: No joints on '{goName}'.";
            if (jointIndex < 0 || jointIndex >= joints.Length)
                return $"Error: Joint index {jointIndex} out of range (0-{joints.Length - 1}).";

            var j = joints[jointIndex];
            Undo.RecordObject(j, "Configure Joint via Agent");

            if (breakForce >= 0) j.breakForce = breakForce;
            if (breakTorque >= 0) j.breakTorque = breakTorque;
            var a = ParseVector3(anchor);
            if (a.HasValue) j.anchor = a.Value;
            var ca = ParseVector3(connectedAnchor);
            if (ca.HasValue) j.connectedAnchor = ca.Value;
            if (enableCollision >= 0) j.enableCollision = enableCollision != 0;
            if (enablePreprocessing >= 0) j.enablePreprocessing = enablePreprocessing != 0;

            EditorUtility.SetDirty(j);
            return $"Success: Configured {j.GetType().Name}[{jointIndex}] on '{goName}'.";
        }

        [AgentTool(@"Configure a HingeJoint. Use -999 for unchanged float values.
motor: targetVelocity (degrees/s) and force. limits: min and max angles (degrees). spring: spring force and damper.")]
        public static string ConfigureHingeJoint(string goName, int jointIndex = 0,
            int useMotor = -1, float motorTargetVelocity = -999f, float motorForce = -999f, int motorFreeSpin = -1,
            int useLimits = -1, float limitMin = -999f, float limitMax = -999f, float limitBounciness = -999f, float limitContactDistance = -999f,
            int useSpring = -1, float spring = -999f, float damper = -999f, float springTargetPosition = -999f,
            string axis = "")
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<HingeJoint>();
            if (joints.Length == 0) return $"Error: No HingeJoint on '{goName}'.";
            if (jointIndex < 0 || jointIndex >= joints.Length)
                return $"Error: HingeJoint index {jointIndex} out of range (0-{joints.Length - 1}).";

            var hj = joints[jointIndex];
            Undo.RecordObject(hj, "Configure HingeJoint via Agent");

            // Motor
            if (useMotor >= 0) hj.useMotor = useMotor != 0;
            if (motorTargetVelocity > -999f || motorForce > -999f || motorFreeSpin >= 0)
            {
                var motor = hj.motor;
                if (motorTargetVelocity > -999f) motor.targetVelocity = motorTargetVelocity;
                if (motorForce > -999f) motor.force = motorForce;
                if (motorFreeSpin >= 0) motor.freeSpin = motorFreeSpin != 0;
                hj.motor = motor;
            }

            // Limits
            if (useLimits >= 0) hj.useLimits = useLimits != 0;
            if (limitMin > -999f || limitMax > -999f || limitBounciness > -999f || limitContactDistance > -999f)
            {
                var limits = hj.limits;
                if (limitMin > -999f) limits.min = limitMin;
                if (limitMax > -999f) limits.max = limitMax;
                if (limitBounciness > -999f) limits.bounciness = limitBounciness;
                if (limitContactDistance > -999f) limits.contactDistance = limitContactDistance;
                hj.limits = limits;
            }

            // Spring
            if (useSpring >= 0) hj.useSpring = useSpring != 0;
            if (spring > -999f || damper > -999f || springTargetPosition > -999f)
            {
                var sp = hj.spring;
                if (spring > -999f) sp.spring = spring;
                if (damper > -999f) sp.damper = damper;
                if (springTargetPosition > -999f) sp.targetPosition = springTargetPosition;
                hj.spring = sp;
            }

            // Axis
            var ax = ParseVector3(axis);
            if (ax.HasValue) hj.axis = ax.Value;

            EditorUtility.SetDirty(hj);
            return $"Success: Configured HingeJoint[{jointIndex}] on '{goName}' (motor={hj.useMotor}, limits={hj.useLimits}[{hj.limits.min}~{hj.limits.max}], spring={hj.useSpring}).";
        }

        [AgentTool("Configure a SpringJoint. Use -999 for unchanged float values. spring: spring force, damper: damping, distances: min/max stretch.")]
        public static string ConfigureSpringJoint(string goName, int jointIndex = 0,
            float spring = -999f, float damper = -999f, float minDistance = -999f, float maxDistance = -999f,
            float tolerance = -999f, float breakForce = -999f, float breakTorque = -999f)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<SpringJoint>();
            if (joints.Length == 0) return $"Error: No SpringJoint on '{goName}'.";
            if (jointIndex < 0 || jointIndex >= joints.Length)
                return $"Error: SpringJoint index {jointIndex} out of range (0-{joints.Length - 1}).";

            var sj = joints[jointIndex];
            Undo.RecordObject(sj, "Configure SpringJoint via Agent");

            if (spring > -999f) sj.spring = spring;
            if (damper > -999f) sj.damper = damper;
            if (minDistance > -999f) sj.minDistance = minDistance;
            if (maxDistance > -999f) sj.maxDistance = maxDistance;
            if (tolerance > -999f) sj.tolerance = tolerance;
            if (breakForce > -999f) sj.breakForce = breakForce;
            if (breakTorque > -999f) sj.breakTorque = breakTorque;

            EditorUtility.SetDirty(sj);
            return $"Success: Configured SpringJoint[{jointIndex}] on '{goName}' (spring={sj.spring}, damper={sj.damper}, dist=[{sj.minDistance}~{sj.maxDistance}]).";
        }

        [AgentTool(@"Configure a ConfigurableJoint axis locks. Motion values: 0=Locked, 1=Limited, 2=Free.
Controls which axes are locked/limited/free for both linear and angular motion.")]
        public static string ConfigureConfigurableJoint(string goName, int jointIndex = 0,
            int xMotion = -1, int yMotion = -1, int zMotion = -1,
            int angularXMotion = -1, int angularYMotion = -1, int angularZMotion = -1)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<ConfigurableJoint>();
            if (joints.Length == 0) return $"Error: No ConfigurableJoint on '{goName}'.";
            if (jointIndex < 0 || jointIndex >= joints.Length)
                return $"Error: ConfigurableJoint index {jointIndex} out of range (0-{joints.Length - 1}).";

            var cj = joints[jointIndex];
            Undo.RecordObject(cj, "Configure ConfigurableJoint via Agent");

            if (xMotion >= 0) cj.xMotion = (ConfigurableJointMotion)xMotion;
            if (yMotion >= 0) cj.yMotion = (ConfigurableJointMotion)yMotion;
            if (zMotion >= 0) cj.zMotion = (ConfigurableJointMotion)zMotion;
            if (angularXMotion >= 0) cj.angularXMotion = (ConfigurableJointMotion)angularXMotion;
            if (angularYMotion >= 0) cj.angularYMotion = (ConfigurableJointMotion)angularYMotion;
            if (angularZMotion >= 0) cj.angularZMotion = (ConfigurableJointMotion)angularZMotion;

            EditorUtility.SetDirty(cj);
            string[] modes = { "Locked", "Limited", "Free" };
            return $"Success: Configured ConfigurableJoint[{jointIndex}] on '{goName}' (linear=[{cj.xMotion},{cj.yMotion},{cj.zMotion}], angular=[{cj.angularXMotion},{cj.angularYMotion},{cj.angularZMotion}]).";
        }

        [AgentTool("Inspect a Joint in detail. Shows all type-specific settings (motor, limits, spring, axis locks).")]
        public static string InspectJoint(string goName, int jointIndex = 0)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var joints = go.GetComponents<Joint>();
            if (joints.Length == 0) return $"Error: No joints on '{goName}'.";
            if (jointIndex < 0 || jointIndex >= joints.Length)
                return $"Error: Joint index {jointIndex} out of range (0-{joints.Length - 1}).";

            var j = joints[jointIndex];
            var sb = new StringBuilder();
            string connected = j.connectedBody != null ? j.connectedBody.name : "none";
            sb.AppendLine($"{j.GetType().Name}[{jointIndex}] on '{goName}':");
            sb.AppendLine($"  ConnectedBody: {connected}");
            sb.AppendLine($"  Anchor: {j.anchor}");
            sb.AppendLine($"  ConnectedAnchor: {j.connectedAnchor}");
            sb.AppendLine($"  Axis: {j.axis}");
            sb.AppendLine($"  BreakForce: {j.breakForce}");
            sb.AppendLine($"  BreakTorque: {j.breakTorque}");
            sb.AppendLine($"  EnableCollision: {j.enableCollision}");
            sb.AppendLine($"  EnablePreprocessing: {j.enablePreprocessing}");

            if (j is HingeJoint hj)
            {
                sb.AppendLine($"  [Motor] use={hj.useMotor}, velocity={hj.motor.targetVelocity}, force={hj.motor.force}, freeSpin={hj.motor.freeSpin}");
                sb.AppendLine($"  [Limits] use={hj.useLimits}, min={hj.limits.min}, max={hj.limits.max}, bounciness={hj.limits.bounciness}");
                sb.AppendLine($"  [Spring] use={hj.useSpring}, spring={hj.spring.spring}, damper={hj.spring.damper}, target={hj.spring.targetPosition}");
                sb.AppendLine($"  Angle: {hj.angle}");
                sb.AppendLine($"  Velocity: {hj.velocity}");
            }
            else if (j is SpringJoint sj)
            {
                sb.AppendLine($"  Spring: {sj.spring}");
                sb.AppendLine($"  Damper: {sj.damper}");
                sb.AppendLine($"  MinDistance: {sj.minDistance}");
                sb.AppendLine($"  MaxDistance: {sj.maxDistance}");
                sb.AppendLine($"  Tolerance: {sj.tolerance}");
            }
            else if (j is ConfigurableJoint cj)
            {
                sb.AppendLine($"  [Linear] x={cj.xMotion}, y={cj.yMotion}, z={cj.zMotion}");
                sb.AppendLine($"  [Angular] x={cj.angularXMotion}, y={cj.angularYMotion}, z={cj.angularZMotion}");
                sb.AppendLine($"  RotationDriveMode: {cj.rotationDriveMode}");
                sb.AppendLine($"  ProjectionMode: {cj.projectionMode}");
            }
            else if (j is CharacterJoint ch)
            {
                sb.AppendLine($"  SwingAxis: {ch.swingAxis}");
                sb.AppendLine($"  LowTwistLimit: {ch.lowTwistLimit.limit}");
                sb.AppendLine($"  HighTwistLimit: {ch.highTwistLimit.limit}");
                sb.AppendLine($"  Swing1Limit: {ch.swing1Limit.limit}");
                sb.AppendLine($"  Swing2Limit: {ch.swing2Limit.limit}");
            }

            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Rigidbody — Advanced
        // =================================================================

        [AgentTool(@"Configure advanced Rigidbody settings. interpolation: 0=None, 1=Interpolate, 2=Extrapolate. collisionDetection: 0=Discrete, 1=Continuous, 2=ContinuousDynamic, 3=ContinuousSpeculative.
constraints is a bitmask: 2=FreezePositionX, 4=Y, 8=Z, 16=FreezeRotationX, 32=Y, 64=Z, 126=FreezeAll. Combine with addition.")]
        public static string ConfigureRigidbodyAdvanced(string goName, int interpolation = -1, int collisionDetection = -1,
            int constraints = -1, float maxAngularVelocity = -1f, float sleepThreshold = -1f, int solverIterations = -1)
        {
            var go = FindGO(goName);
            if (go == null) return NotFound(goName);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) return $"Error: No Rigidbody on '{goName}'.";

            Undo.RecordObject(rb, "Configure Rigidbody Advanced");

            if (interpolation >= 0) rb.interpolation = (RigidbodyInterpolation)interpolation;
            if (collisionDetection >= 0) rb.collisionDetectionMode = (CollisionDetectionMode)collisionDetection;
            if (constraints >= 0) rb.constraints = (RigidbodyConstraints)constraints;
            if (maxAngularVelocity >= 0) rb.maxAngularVelocity = maxAngularVelocity;
            if (sleepThreshold >= 0) rb.sleepThreshold = sleepThreshold;
            if (solverIterations >= 0) rb.solverIterations = solverIterations;

            EditorUtility.SetDirty(rb);
            return $"Success: Configured Rigidbody advanced settings on '{goName}' (interpolation={rb.interpolation}, collision={rb.collisionDetectionMode}, constraints={rb.constraints}).";
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        private static string NotFound(string name) => $"Error: GameObject '{name}' not found.";

        private static Vector3? ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }

        private static string GetRelativePath(Transform child, Transform root)
        {
            if (child == root) return "";
            var sb = new StringBuilder(child.name);
            var current = child.parent;
            while (current != null && current != root)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }
    }
}
