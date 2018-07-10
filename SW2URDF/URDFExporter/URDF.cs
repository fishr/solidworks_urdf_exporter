﻿/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using System.Xml;

using SolidWorks.Interop.sldworks;

namespace SW2URDF
{
    //Initiates the XMLWriter and its necessary settings
    public class URDFWriter
    {
        public XmlWriter writer;

        public URDFWriter(string savePath)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = true,
                Indent = true,
                NewLineOnAttributes = true,
            };
            writer = XmlWriter.Create(savePath, settings);
        }
    }

    //Not sure why I have a class that everything else inherits from that is empty. But maybe we'll want to add things to it
    public class URDFElement
    {
        protected static readonly log4net.ILog logger = Logger.GetLogger();

        public URDFElement()
        {
        }

        public void WriteURDF(XmlWriter writer)
        {
        }

        protected bool isRequired;
    }

    public class Attribute
    {
        private readonly string USStringFormat = "en-US";
        public bool isRequired;
        public string type;
        public object value;

        public Attribute()
        {
            type = "";
            isRequired = false;
        }

        public void WriteURDF(XmlWriter writer)
        {
            string value_string = "";
            if (value.GetType() == typeof(double[]))
            {
                double[] value_array = (double[])value;
                foreach (double d in value_array)
                {
                    value_string += d.ToString(CultureInfo.CreateSpecificCulture(USStringFormat)) + " ";
                }
                value_string = value_string.Trim();
            }
            else if (value.GetType() == typeof(double))
            {
                value_string = ((Double)value).ToString(CultureInfo.CreateSpecificCulture(USStringFormat));
            }
            else if (value.GetType() == typeof(string))
            {
                value_string = (string)value;
            }
            else if (value != null)
            {
                throw new Exception("Unhandled object type in write attribute");
            }
            if (isRequired && value == null)
            {
                throw new Exception("Required attribute has null value");
            }
            if (String.IsNullOrWhiteSpace(type))
            {
                throw new Exception("No type specified");
            }
            if (value != null)
            {
                writer.WriteAttributeString(type, value_string);
            }
        }
    }

    //The base URDF element, a robot
    public class Robot : URDFElement
    {
        public Link BaseLink;
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        public Robot()
        {
            BaseLink = new Link(null);
            isRequired = true;
            NameAttribute = new Attribute
            {
                isRequired = true,
                type = "name",
            };
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("robot");
            NameAttribute.WriteURDF(writer);

            BaseLink.WriteURDF(writer);

            writer.WriteEndElement();
            writer.WriteEndDocument();
            writer.Close();
        }

        internal string[] GetJointNames(bool includeFixed)
        {
            return BaseLink.GetJointNames(includeFixed);
        }
    }

    //The link class, it contains many other elements not found in the URDF.
    public class Link : URDFElement
    {
        public Link Parent;
        public List<Link> Children;
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        public Inertial Inertial;
        public Visual Visual;
        public Collision Collision;
        public Joint Joint;
        public bool STLQualityFine;
        public bool isIncomplete;
        public bool isFixedFrame;
        public string CoordSysName;

        // The SW part component object
        public Component2 SWComponent;

        public Component2 SWMainComponent;
        public List<Component2> SWcomponents;
        public List<byte[]> SWComponentPIDs;
        public byte[] SWMainComponentPID;

        public Link(Link parent)
        {
            Parent = parent;
            Children = new List<Link>();
            SWcomponents = new List<Component2>();
            NameAttribute = new Attribute
            {
                isRequired = true,
                type = "name",
            };

            isRequired = true;
            isFixedFrame = true;
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("link");
            NameAttribute.WriteURDF(writer);

            if (Inertial != null)
            {
                Inertial.WriteURDF(writer);
            }
            if (Visual != null)
            {
                Visual.WriteURDF(writer);
            }
            if (Collision != null)
            {
                Collision.WriteURDF(writer);
            }

            writer.WriteEndElement();
            if (Joint != null && Joint.Name != null)
            {
                Joint.WriteURDF(writer);
            }

            foreach (Link child in Children)
            {
                child.WriteURDF(writer);
            }
        }

        public String[] GetJointNames(bool includeFixed)
        {
            List<String> names = new List<string>();

            if (Joint != null && (includeFixed || Joint.Type != "fixed"))
            {
                names.Add(Joint.Name);
            }
            foreach (Link child in Children)
            {
                names.AddRange(child.GetJointNames(includeFixed));
            }

            return names.ToArray();
        }
    }

    //The serial node class, it is used only for saving the configuration.
    public class SerialNode
    {
        public string linkName;
        public string jointName;
        public string axisName;
        public string coordsysName;
        public List<byte[]> componentPIDs;
        public string jointType;
        public bool isBaseNode;
        public bool isIncomplete;
        public List<SerialNode> Nodes;

        //This is only used by the serialization module.
        public SerialNode()
        {
            Nodes = new List<SerialNode>();
        }

        public SerialNode(LinkNode node)
        {
            Nodes = new List<SerialNode>();
            if (node.Link == null)
            {
                linkName = node.LinkName;
                jointName = node.JointName;
                axisName = node.AxisName;
                coordsysName = node.CoordsysName;
                componentPIDs = node.ComponentPIDs;
                jointType = node.JointType;
                isBaseNode = node.IsBaseNode;
                isIncomplete = node.IsIncomplete;
            }
            else
            {
                linkName = node.Link.Name;

                componentPIDs = node.ComponentPIDs;
                if (node.Link.Joint != null)
                {
                    jointName = node.Link.Joint.Name;

                    if (node.Link.Joint.Axis.X == 0 && node.Link.Joint.Axis.Y == 0 && node.Link.Joint.Axis.Z == 0)
                    {
                        axisName = "None";
                    }
                    else
                    {
                        axisName = node.Link.Joint.AxisName;
                    }
                    coordsysName = node.Link.Joint.CoordinateSystemName;
                    jointType = node.Link.Joint.Type;
                }
                else
                {
                    coordsysName = node.CoordsysName;
                }

                isBaseNode = node.IsBaseNode;
                isIncomplete = node.IsIncomplete;
            }
            //Proceed recursively through the nodes
            foreach (LinkNode child in node.Nodes)
            {
                Nodes.Add(new SerialNode(child));
            }
        }
    }

    //The inertial element of a link
    public class Inertial : URDFElement
    {
        public Origin Origin;
        public Mass Mass;
        public Inertia Inertia;

        public Inertial()
        {
            Origin = new Origin();
            Mass = new Mass();
            Inertia = new Inertia();
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("inertial");

            Origin.WriteURDF(writer);
            Mass.WriteURDF(writer);
            Inertia.WriteURDF(writer);

            writer.WriteEndElement();
        }
    }

    //The Origin element, used in several other elements
    public class Origin : URDFElement
    {
        private Attribute XYZAttribute;
        private Attribute RPYAttribute;

        private double[] XYZ
        {
            get
            {
                return (double[])XYZAttribute.value;
            }
            set
            {
                XYZAttribute.value = value;
            }
        }

        public double[] GetXYZ()
        {
            return (double[])XYZ.Clone();
        }

        public void SetXYZ(double[] xyz)
        {
            XYZ = xyz;
        }

        public double X
        {
            get
            {
                return XYZ[0];
            }
            set
            {
                XYZ[0] = value;
            }
        }

        public double Y
        {
            get
            {
                return XYZ[1];
            }
            set
            {
                XYZ[1] = value;
            }
        }

        public double Z
        {
            get
            {
                return XYZ[2];
            }
            set
            {
                XYZ[2] = value;
            }
        }

        private double[] RPY
        {
            get
            {
                return (double[])RPYAttribute.value;
            }
            set
            {
                RPYAttribute.value = value;
            }
        }

        public double[] GetRPY()
        {
            return (double[])RPY.Clone();
        }

        public void SetRPY(double[] rpy)
        {
            RPY = rpy;
        }

        public double Roll
        {
            get
            {
                return RPY[0];
            }
            set
            {
                RPY[0] = value;
            }
        }

        public double Pitch
        {
            get
            {
                return RPY[1];
            }
            set
            {
                RPY[1] = value;
            }
        }

        public double Yaw
        {
            get
            {
                return RPY[2];
            }
            set
            {
                RPY[2] = value;
            }
        }

        public bool isCustomized;

        public Origin()
        {
            isCustomized = false;
            XYZAttribute = new Attribute();
            RPYAttribute = new Attribute();
            XYZAttribute.type = "xyz";
            RPYAttribute.type = "rpy";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("origin");
            XYZAttribute.WriteURDF(writer);
            RPYAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_x, TextBox box_y, TextBox box_z, TextBox box_roll, TextBox box_pitch, TextBox box_yaw, string format)
        {
            if (XYZAttribute.value != null)
            {
                box_x.Text = X.ToString(format);
                box_y.Text = Y.ToString(format);
                box_z.Text = Z.ToString(format);
            }
            else
            {
                box_x.Text = ""; box_y.Text = ""; box_z.Text = "";
            }

            if (RPYAttribute.value != null)
            {
                box_roll.Text = Roll.ToString(format);
                box_pitch.Text = Pitch.ToString(format);
                box_yaw.Text = Yaw.ToString(format);
            }
            else
            {
                box_roll.Text = ""; box_pitch.Text = ""; box_yaw.Text = "";
            }
        }

        public void Update(TextBox box_x, TextBox box_y, TextBox box_z, TextBox box_roll, TextBox box_pitch, TextBox box_yaw)
        {
            double value;
            if (String.IsNullOrWhiteSpace(box_x.Text) && String.IsNullOrWhiteSpace(box_y.Text) && String.IsNullOrWhiteSpace(box_z.Text))
            {
                XYZ = null;
            }
            else
            {
                X = (Double.TryParse(box_x.Text, out value)) ? value : 0;
                Y = (Double.TryParse(box_y.Text, out value)) ? value : 0;
                Z = (Double.TryParse(box_z.Text, out value)) ? value : 0;
            }

            if (String.IsNullOrWhiteSpace(box_roll.Text) && String.IsNullOrWhiteSpace(box_pitch.Text) && String.IsNullOrWhiteSpace(box_yaw.Text))
            {
                RPY = null;
            }
            else
            {
                Roll = (Double.TryParse(box_roll.Text, out value)) ? value : 0;
                Pitch = (Double.TryParse(box_pitch.Text, out value)) ? value : 0;
                Yaw = (Double.TryParse(box_yaw.Text, out value)) ? value : 0;
            }
        }
    }

    //mass element, belongs to the inertial element
    public class Mass : URDFElement
    {
        private Attribute ValueAttribute;

        public double Value
        {
            get
            {
                return (double)ValueAttribute.value;
            }
            set
            {
                ValueAttribute.value = value;
            }
        }

        public Mass()
        {
            ValueAttribute = new Attribute
            {
                type = "value",
                isRequired = true,
            };
            Value = 0.0;
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("mass");
            ValueAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box, string format)
        {
            if (ValueAttribute != null)
            {
                box.Text = Value.ToString(format);
            }
            else
            {
                box.Text = "0";
            }
        }

        public void Update(TextBox box)
        {
            Value = (Double.TryParse(box.Text, out double tmp)) ? tmp : 0;
        }
    }

    //Inertia element, which means moment of inertia. In the inertial element
    public class Inertia : URDFElement
    {
        private Attribute IxxAttribute;

        public double Ixx
        {
            get
            {
                return (double)IxxAttribute.value;
            }
            set
            {
                IxxAttribute.value = value;
            }
        }

        private Attribute IxyAttribute;

        public double Ixy
        {
            get
            {
                return (double)IxyAttribute.value;
            }
            set
            {
                IxyAttribute.value = value;
            }
        }

        private Attribute IxzAttribute;

        public double Ixz
        {
            get
            {
                return (double)IxzAttribute.value;
            }
            set
            {
                IxzAttribute.value = value;
            }
        }

        private Attribute IyyAttribute;

        public double Iyy
        {
            get
            {
                return (double)IyyAttribute.value;
            }
            set
            {
                IyyAttribute.value = value;
            }
        }

        private Attribute IyzAttribute;

        public double Iyz
        {
            get
            {
                return (double)IyzAttribute.value;
            }
            set
            {
                IyzAttribute.value = value;
            }
        }

        private Attribute IzzAttribute;

        public double Izz
        {
            get
            {
                return (double)IzzAttribute.value;
            }
            set
            {
                IzzAttribute.value = value;
            }
        }

        private double[] Moment { get; set; }

        public Inertia()
        {
            Moment = new double[9] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            IxxAttribute = new Attribute();
            IxyAttribute = new Attribute();
            IxzAttribute = new Attribute();
            IyyAttribute = new Attribute();
            IyzAttribute = new Attribute();
            IzzAttribute = new Attribute();

            IxxAttribute.isRequired = true;
            IxxAttribute.type = "ixx";
            Ixx = 0.0;
            IxyAttribute.isRequired = true;
            IxyAttribute.type = "ixy";
            Ixy = 0.0;
            IxzAttribute.isRequired = true;
            IxzAttribute.type = "ixz";
            Ixz = 0.0;
            IyyAttribute.isRequired = true;
            IyyAttribute.type = "iyy";
            Iyy = 0.0;
            IyzAttribute.isRequired = true;
            IyzAttribute.type = "iyz";
            Iyz = 0.0;
            IzzAttribute.isRequired = true;
            IzzAttribute.type = "izz";
            Izz = 0.0;
        }

        public void SetMomentMatrix(double[] array)
        {
            Moment = (double[])array.Clone();
            Ixx = Moment[0];
            Ixy = -Moment[1];
            Ixz = -Moment[2];
            Iyy = Moment[4];
            Iyz = -Moment[5];
            Izz = Moment[8];
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("inertia");
            IxxAttribute.WriteURDF(writer);
            IxyAttribute.WriteURDF(writer);
            IxzAttribute.WriteURDF(writer);
            IyyAttribute.WriteURDF(writer);
            IyzAttribute.WriteURDF(writer);
            IzzAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_ixx, TextBox box_ixy, TextBox box_ixz, TextBox box_iyy, TextBox box_iyz, TextBox box_izz, string format)
        {
            box_ixx.Text = Ixx.ToString(format);
            box_ixy.Text = Ixy.ToString(format);
            box_ixz.Text = Ixz.ToString(format);
            box_iyy.Text = Iyy.ToString(format);
            box_iyz.Text = Iyz.ToString(format);
            box_izz.Text = Izz.ToString(format);
        }

        public void Update(TextBox box_ixx, TextBox box_ixy, TextBox box_ixz, TextBox box_iyy, TextBox box_iyz, TextBox box_izz)
        {
            double value = 0;
            Ixx = (Double.TryParse(box_ixx.Text, out value)) ? value : 0;
            Ixy = (Double.TryParse(box_ixy.Text, out value)) ? value : 0;
            Ixz = (Double.TryParse(box_ixz.Text, out value)) ? value : 0;
            Iyy = (Double.TryParse(box_iyy.Text, out value)) ? value : 0;
            Iyz = (Double.TryParse(box_iyz.Text, out value)) ? value : 0;
            Izz = (Double.TryParse(box_izz.Text, out value)) ? value : 0;
        }

        internal double[] GetMoment()
        {
            return (double[])Moment.Clone();
        }
    }

    //The visual element of a link
    public class Visual : URDFElement
    {
        public Origin Origin;
        public Geometry Geometry;
        public Material Material;

        public Visual()
        {
            Origin = new Origin();
            Geometry = new Geometry();
            Material = new Material();
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("visual");

            Origin.WriteURDF(writer);
            Geometry.WriteURDF(writer);
            Material.WriteURDF(writer);

            writer.WriteEndElement();
        }
    }

    //The geometry element the visual element
    public class Geometry : URDFElement
    {
        public Mesh Mesh;

        public Geometry()
        {
            Mesh = new Mesh();
            isRequired = true;
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("geometry");

            Mesh.WriteURDF(writer);

            writer.WriteEndElement();
        }
    }

    //The mesh element of the geometry element. This contains only a filename location of the mesh.
    public class Mesh : URDFElement
    {
        private Attribute FilenameAttribute;

        public string Filename
        {
            get
            {
                return (string)FilenameAttribute.value;
            }
            set
            {
                FilenameAttribute.value = value;
            }
        }

        public Mesh()
        {
            FilenameAttribute = new Attribute();
            FilenameAttribute.isRequired = true;
            FilenameAttribute.type = "filename";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("mesh");
            FilenameAttribute.WriteURDF(writer);
            writer.WriteEndElement(); //mesh
        }
    }

    //The material element of the visual element.
    public class Material : URDFElement
    {
        public Color Color;
        public Texture Texture;
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        public Material()
        {
            Color = new Color();
            Texture = new Texture();
            NameAttribute = new Attribute();
            NameAttribute.value = "";
            NameAttribute.isRequired = true;
            NameAttribute.type = "name";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("material");
            NameAttribute.WriteURDF(writer);

            Color.WriteURDF(writer);
            Texture.WriteURDF(writer);

            writer.WriteEndElement();
        }

        public void FillBoxes(ComboBox box, string format)
        {
            box.Text = Name;
        }
    }

    //The color element of the material element. Contains a single RGBA.
    public class Color : URDFElement
    {
        private Attribute RGBAAttribute;

        private double[] RGBA
        {
            get
            {
                return (double[])RGBAAttribute.value;
            }
            set
            {
                RGBAAttribute.value = value;
            }
        }

        public double Red
        {
            get
            {
                return RGBA[0];
            }
            set
            {
                RGBA[0] = value;
            }
        }

        public double Green
        {
            get
            {
                return RGBA[1];
            }
            set
            {
                RGBA[1] = value;
            }
        }

        public double Blue
        {
            get
            {
                return RGBA[2];
            }
            set
            {
                RGBA[2] = value;
            }
        }

        public double Alpha
        {
            get
            {
                return RGBA[3];
            }
            set
            {
                RGBA[3] = value;
            }
        }

        public Color()
        {
            RGBAAttribute = new Attribute();
            RGBAAttribute.isRequired = true;
            RGBAAttribute.type = "rgba";
            RGBA = new double[4] { 1, 1, 1, 1 };
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("color");
            RGBAAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(DomainUpDown box_red, DomainUpDown box_green, DomainUpDown box_blue, DomainUpDown box_alpha, string format)
        {
            double[] rgba = (double[])RGBAAttribute.value;
            box_red.Text = Red.ToString(format);
            box_green.Text = Green.ToString(format);
            box_blue.Text = Blue.ToString(format);
            box_alpha.Text = Alpha.ToString(format);
        }

        public void Update(DomainUpDown box_red, DomainUpDown box_green, DomainUpDown box_blue, DomainUpDown box_alpha)
        {
            double value;
            Red = (Double.TryParse(box_red.Text, out value)) ? value : 0;
            Green = (Double.TryParse(box_green.Text, out value)) ? value : 0;
            Blue = (Double.TryParse(box_blue.Text, out value)) ? value : 0;
            Alpha = (Double.TryParse(box_alpha.Text, out value)) ? value : 0;
        }
    }

    //The texture element of the material element.
    public class Texture : URDFElement
    {
        private Attribute FilenameAttribute;

        public string Filename
        {
            get
            {
                return (string)FilenameAttribute.value;
            }
            set
            {
                FilenameAttribute.value = value;
            }
        }

        public string wFilename;

        public Texture()
        {
            wFilename = "";
            isRequired = false;
            FilenameAttribute = new Attribute();
            FilenameAttribute.isRequired = true;
            Filename = "";
            FilenameAttribute.type = "filename";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            if (!String.IsNullOrWhiteSpace(wFilename))
            {
                writer.WriteStartElement("texture");
                FilenameAttribute.WriteURDF(writer);
                writer.WriteEndElement();
            }
        }
    }

    //The collision element of a link.
    public class Collision : URDFElement
    {
        public Origin Origin;
        public Geometry Geometry;

        public Collision()
        {
            Origin = new Origin();
            Geometry = new Geometry();
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("collision");

            Origin.WriteURDF(writer);
            Geometry.WriteURDF(writer);

            writer.WriteEndElement();
        }
    }

    //The joint class. There is one for every link but the base link
    public class Joint : URDFElement
    {
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        private Attribute TypeAttribute;

        public string Type
        {
            get
            {
                return (string)TypeAttribute.value;
            }
            set
            {
                TypeAttribute.value = value;
            }
        }

        public Origin Origin;
        public ParentLink Parent;
        public ChildLink Child;
        public Axis Axis;
        public Limit Limit;
        public Calibration Calibration;
        public Dynamics Dynamics;
        public SafetyController Safety;
        public string CoordinateSystemName;
        public string AxisName;

        public Joint()
        {
            Origin = new Origin();
            Parent = new ParentLink();
            Child = new ChildLink();
            Axis = new Axis();
            NameAttribute = new Attribute();
            NameAttribute.isRequired = true;
            NameAttribute.type = "name";
            Name = "";
            TypeAttribute = new Attribute();
            TypeAttribute.isRequired = true;
            TypeAttribute.type = "type";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("joint");
            NameAttribute.WriteURDF(writer);
            TypeAttribute.WriteURDF(writer);
            //writer.WriteAttributeString("name", "joint_" + name);
            //writer.WriteAttributeString("type", type);

            Origin.WriteURDF(writer);
            Parent.WriteURDF(writer);
            Child.WriteURDF(writer);
            Axis.WriteURDF(writer);
            if (Limit != null)
            {
                Limit.WriteURDF(writer);
            }
            if (Calibration != null)
            {
                Calibration.WriteURDF(writer);
            }
            if (Dynamics != null)
            {
                Dynamics.WriteURDF(writer);
            }
            if (Safety != null)
            {
                Safety.WriteURDF(writer);
            }
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_name, ComboBox box_type)
        {
            box_name.Text = Name;
            box_type.Text = Type;
        }

        public void Update(TextBox box_name, ComboBox box_type)
        {
            Name = box_name.Text;
            Type = box_type.Text;
        }
    }

    //parent_link element of a joint.
    public class ParentLink : URDFElement
    {
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        public ParentLink()
        {
            isRequired = true;
            NameAttribute = new Attribute();
            NameAttribute.isRequired = true;
            NameAttribute.type = "link";
            Name = "";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("parent");
            NameAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(Label box)
        {
            box.Text = Name;
        }

        public void Update(Label box)
        {
            Name = box.Text;
        }
    }

    //The child link element
    public class ChildLink : URDFElement
    {
        private Attribute NameAttribute;

        public string Name
        {
            get
            {
                return (string)NameAttribute.value;
            }
            set
            {
                NameAttribute.value = value;
            }
        }

        public ChildLink()
        {
            isRequired = true;
            NameAttribute = new Attribute();
            NameAttribute.type = "link";
            NameAttribute.isRequired = true;
            Name = "";
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("child");
            NameAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(Label box)
        {
            box.Text = Name;
        }

        public void Update(Label box)
        {
            Name = box.Text;
        }
    }

    //The axis element of a joint.
    public class Axis : URDFElement
    {
        private Attribute XYZAttribute;

        private double[] XYZ
        {
            get
            {
                return (double[])XYZAttribute.value;
            }
            set
            {
                XYZAttribute.value = value;
            }
        }

        public double[] GetXYZ()
        {
            return (double[])XYZ.Clone();
        }

        public void SetXYZ(double[] xyz)
        {
            XYZ = (double[])xyz.Clone();
        }

        public double X
        {
            get
            {
                return XYZ[0];
            }
            set
            {
                XYZ[0] = value;
            }
        }

        public double Y
        {
            get
            {
                return XYZ[1];
            }
            set
            {
                XYZ[1] = value;
            }
        }

        public double Z
        {
            get
            {
                return XYZ[2];
            }
            set
            {
                XYZ[2] = value;
            }
        }

        public Axis()
        {
            XYZAttribute = new Attribute();
            XYZAttribute.isRequired = true;
            XYZAttribute.type = "xyz";
            XYZ = new double[] { 0, 0, 0 };
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("axis");
            XYZAttribute.WriteURDF(writer);
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_x, TextBox box_y, TextBox box_z, string format)
        {
            box_x.Text = X.ToString(format);
            box_y.Text = Y.ToString(format);
            box_z.Text = Z.ToString(format);
        }

        public void Update(TextBox box_x, TextBox box_y, TextBox box_z)
        {
            double value;
            X = (Double.TryParse(box_x.Text, out value)) ? value : 0;
            Y = (Double.TryParse(box_y.Text, out value)) ? value : 0;
            Z = (Double.TryParse(box_z.Text, out value)) ? value : 0;
        }
    }

    //The limit element of a joint.
    public class Limit : URDFElement
    {
        private Attribute LowerAttribute;
        private Attribute UpperAttribute;
        private Attribute EffortAttribute;
        private Attribute VelocityAttribute;

        public double Lower
        {
            get
            {
                return (double)LowerAttribute.value;
            }
            set
            {
                if (LowerAttribute == null)
                {
                    LowerAttribute = new Attribute();
                    LowerAttribute.type = "lower";
                }
                LowerAttribute.value = value;
            }
        }

        public double Upper
        {
            get
            {
                return (double)UpperAttribute.value;
            }
            set
            {
                if (UpperAttribute == null)
                {
                    UpperAttribute = new Attribute();
                    UpperAttribute.type = "upper";
                }
                UpperAttribute.value = value;
            }
        }

        public double Effort
        {
            get
            {
                return (double)EffortAttribute.value;
            }
            set
            {
                EffortAttribute.value = value;
            }
        }

        public double Velocity
        {
            get
            {
                return (double)VelocityAttribute.value;
            }
            set
            {
                VelocityAttribute.value = value;
            }
        }

        public Limit()
        {
            EffortAttribute = new Attribute();
            VelocityAttribute = new Attribute();
            EffortAttribute.isRequired = true;
            VelocityAttribute.isRequired = true;
            EffortAttribute.type = "effort";
            VelocityAttribute.type = "velocity";
            EffortAttribute.value = 0.0;
            VelocityAttribute.value = 0.0;
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("limit");
            if (LowerAttribute != null)
            {
                LowerAttribute.WriteURDF(writer);
            }
            if (UpperAttribute != null)
            {
                UpperAttribute.WriteURDF(writer);
            }
            if (EffortAttribute != null)
            {
                EffortAttribute.WriteURDF(writer);
            }
            if (VelocityAttribute != null)
            {
                VelocityAttribute.WriteURDF(writer);
            }
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_lower, TextBox box_upper, TextBox box_effort, TextBox box_velocity, string format)
        {
            if (LowerAttribute != null)
            {
                box_lower.Text = Lower.ToString(format);
            }

            if (UpperAttribute != null)
            {
                box_upper.Text = Upper.ToString(format);
            }

            box_effort.Text = Effort.ToString(format);
            box_velocity.Text = Velocity.ToString(format);
        }

        public void SetValues(TextBox box_lower, TextBox box_upper, TextBox box_effort, TextBox box_velocity)
        {
            double value;
            if (String.IsNullOrWhiteSpace(box_lower.Text))
            {
                LowerAttribute = null;
            }
            else
            {
                Lower = (Double.TryParse(box_lower.Text, out value)) ? value : 0;
            }
            if (String.IsNullOrWhiteSpace(box_upper.Text))
            {
                UpperAttribute = null;
            }
            else
            {
                Upper = (Double.TryParse(box_upper.Text, out value)) ? value : 0;
            }

            Effort = (Double.TryParse(box_effort.Text, out value)) ? value : 0;
            Velocity = (Double.TryParse(box_velocity.Text, out value)) ? value : 0;
        }
    }

    //The calibration element of a joint.
    public class Calibration : URDFElement
    {
        private Attribute RisingAttribute;

        public double Rising
        {
            get
            {
                return (double)RisingAttribute.value;
            }
            set
            {
                if (RisingAttribute == null)
                {
                    RisingAttribute = new Attribute();
                    RisingAttribute.type = "rising";
                }
                RisingAttribute.value = value;
            }
        }

        private Attribute FallingAttribute;

        public double Falling
        {
            get
            {
                return (double)FallingAttribute.value;
            }
            set
            {
                if (FallingAttribute == null)
                {
                    FallingAttribute = new Attribute();
                    FallingAttribute.type = "falling";
                }

                FallingAttribute.value = value;
            }
        }

        public Calibration()
        {
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("calibration");
            if (RisingAttribute != null)
            {
                RisingAttribute.WriteURDF(writer);
            }
            if (FallingAttribute != null)
            {
                FallingAttribute.WriteURDF(writer);
            }
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_rising, TextBox box_falling, string format)
        {
            if (RisingAttribute != null)
            {
                box_rising.Text = Rising.ToString(format);
            }

            if (FallingAttribute != null)
            {
                box_falling.Text = Falling.ToString(format);
            }
        }

        public void SetValues(TextBox box_rising, TextBox box_falling)
        {
            double value;
            if (String.IsNullOrWhiteSpace(box_rising.Text))
            {
                RisingAttribute = null;
            }
            else
            {
                Rising = (Double.TryParse(box_rising.Text, out value)) ? value : 0;
            }
            if (String.IsNullOrWhiteSpace(box_falling.Text))
            {
                FallingAttribute = null;
            }
            else
            {
                Falling = (Double.TryParse(box_falling.Text, out value)) ? value : 0;
            }
        }
    }

    //The dynamics element of a joint.
    public class Dynamics : URDFElement
    {
        private Attribute DampingAttribute;

        public double Damping
        {
            get
            {
                return (double)DampingAttribute.value;
            }
            set
            {
                if (DampingAttribute == null)
                {
                    DampingAttribute = new Attribute();
                    DampingAttribute.type = "damping";
                }
                DampingAttribute.value = value;
            }
        }

        private Attribute FrictionAttribute;

        public double Friction
        {
            get
            {
                return (double)FrictionAttribute.value;
            }
            set
            {
                if (FrictionAttribute == null)
                {
                    FrictionAttribute = new Attribute();
                    FrictionAttribute.type = "friction";
                }
                FrictionAttribute.value = value;
            }
        }

        public Dynamics()
        {
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("dynamics");
            if (DampingAttribute != null)
            {
                DampingAttribute.WriteURDF(writer);
            }
            if (FrictionAttribute != null)
            {
                FrictionAttribute.WriteURDF(writer);
            }
            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_damping, TextBox box_friction, string format)
        {
            if (DampingAttribute != null)
            {
                box_damping.Text = Damping.ToString(format);
            }
            if (FrictionAttribute != null)
            {
                box_friction.Text = Friction.ToString(format);
            }
        }

        public void SetValues(TextBox box_damping, TextBox box_friction)
        {
            double value;
            if (String.IsNullOrWhiteSpace(box_damping.Text))
            {
                DampingAttribute = null;
            }
            else
            {
                Damping = (Double.TryParse(box_damping.Text, out value)) ? value : 0;
            }
            if (String.IsNullOrWhiteSpace(box_friction.Text))
            {
                FrictionAttribute = null;
            }
            else
            {
                Friction = (Double.TryParse(box_friction.Text, out value)) ? value : 0;
            }
        }
    }

    //The safety_controller element of a joint.
    public class SafetyController : URDFElement
    {
        private Attribute SoftLowerAttribute;

        public double SoftLower
        {
            get
            {
                return (double)SoftLowerAttribute.value;
            }
            set
            {
                if (SoftLowerAttribute == null)
                {
                    SoftLowerAttribute = new Attribute();
                    SoftLowerAttribute.type = "soft_lower";
                }
                SoftLowerAttribute.value = value;
            }
        }

        private Attribute SoftUpperAttribute;

        public double SoftUpper
        {
            get
            {
                return (double)SoftUpperAttribute.value;
            }
            set
            {
                if (SoftUpperAttribute == null)
                {
                    SoftUpperAttribute = new Attribute();
                    SoftUpperAttribute.type = "soft_upper";
                }
                SoftUpperAttribute.value = value;
            }
        }

        private Attribute KPositionAttribute;

        public double KPosition
        {
            get
            {
                return (double)KPositionAttribute.value;
            }
            set
            {
                if (KPositionAttribute == null)
                {
                    KPositionAttribute = new Attribute();
                    KPositionAttribute.type = "k_position";
                }
                KPositionAttribute.value = value;
            }
        }

        private Attribute KVelocityAttribute;

        public double KVelocity
        {
            get
            {
                return (double)KVelocityAttribute.value;
            }
            set
            {
                KVelocityAttribute.value = value;
            }
        }

        public SafetyController()
        {
            KVelocityAttribute = new Attribute();
            KVelocityAttribute.type = "k_velocity";
            KVelocityAttribute.isRequired = true;
        }

        public new void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("safety_controller");
            if (SoftUpperAttribute != null)
            {
                SoftUpperAttribute.WriteURDF(writer);
            }
            if (SoftLowerAttribute != null)
            {
                SoftLowerAttribute.WriteURDF(writer);
            }
            if (KPositionAttribute != null)
            {
                KPositionAttribute.WriteURDF(writer);
            }
            KVelocityAttribute.WriteURDF(writer);

            writer.WriteEndElement();
        }

        public void FillBoxes(TextBox box_lower, TextBox box_upper, TextBox box_position, TextBox box_velocity, string format)
        {
            if (SoftLowerAttribute != null)
            {
                box_lower.Text = SoftLower.ToString(format);
            }

            if (SoftUpperAttribute != null)
            {
                box_upper.Text = SoftUpper.ToString(format);
            }

            if (KPositionAttribute != null)
            {
                box_position.Text = KPosition.ToString(format);
            }

            box_velocity.Text = KVelocity.ToString(format);
        }

        public void SetValues(TextBox box_lower, TextBox box_upper, TextBox box_position, TextBox box_velocity)
        {
            double value;
            if (String.IsNullOrWhiteSpace(box_lower.Text))
            {
                SoftLowerAttribute = null;
            }
            else
            {
                SoftLower = (Double.TryParse(box_lower.Text, out value)) ? value : 0;
            }

            if (String.IsNullOrWhiteSpace(box_upper.Text))
            {
                SoftUpperAttribute = null;
            }
            else
            {
                SoftUpper = (Double.TryParse(box_upper.Text, out value)) ? value : 0;
            }

            if (String.IsNullOrWhiteSpace(box_position.Text))
            {
                KPositionAttribute = null;
            }
            else
            {
                KPosition = (Double.TryParse(box_position.Text, out value)) ? value : 0;
            }
            KVelocity = (Double.TryParse(box_velocity.Text, out value)) ? value : 0;
        }
    }

    //A class that just writes the bare minimum of the manifest file necessary for ROS packages.
    public class PackageXMLWriter
    {
        public XmlWriter writer;
        private static readonly log4net.ILog logger = Logger.GetLogger();

        public PackageXMLWriter(string savePath)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.OmitXmlDeclaration = true;
            settings.Indent = true;
            settings.NewLineOnAttributes = false;
            logger.Info("Creating package.xml at " + savePath);
            writer = XmlWriter.Create(savePath, settings);
        }
    }

    //The base class for packageXML elements. Again, I guess I like having empty base classes
    public class PackageElement
    {
        public PackageElement()
        {
        }

        public void WriteElement()
        {
        }
    }

    //Top level class for the package XML file.
    public class PackageXML : PackageElement
    {
        public Description description;
        public Dependencies dependencies;
        public Author author;
        public License license;

        public PackageXML(string name)
        {
            description = new Description(name);

            dependencies = new Dependencies(
                new String[] { "catkin" },
                new String[] { "roslaunch", "robot_state_publisher", "rviz", "joint_state_publisher", "gazebo" });

            author = new Author("TODO");

            license = new License("BSD");
        }

        public void WriteElement(PackageXMLWriter mWriter)
        {
            XmlWriter writer = mWriter.writer;
            writer.WriteStartDocument();
            writer.WriteStartElement("package");

            description.WriteElement(writer);

            author.WriteElement(writer);
            license.WriteElement(writer);
            dependencies.WriteElement(writer);

            writer.WriteStartElement("export");

            writer.WriteStartElement("architecture_independent");
            writer.WriteEndElement();

            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
            writer.Close();
        }
    }

    //description element of the manifest file
    public class Description : PackageElement
    {
        private readonly string name;
        private readonly string brief;
        private readonly string longDescription;

        public Description(string name)
        {
            this.name = name;
            brief = "URDF Description package for " + name;
            longDescription = "This package contains configuration data, 3D models and launch files\r\n" +
                                    "for " + name + " robot";
        }

        public void WriteElement(XmlWriter writer)
        {
            writer.WriteStartElement("name");
            writer.WriteString(name);
            writer.WriteEndElement();

            writer.WriteStartElement("version");
            writer.WriteString("1.0.0");
            writer.WriteEndElement();

            writer.WriteStartElement("description");

            writer.WriteStartElement("p");
            writer.WriteString(brief);
            writer.WriteEndElement();

            writer.WriteStartElement("p");
            writer.WriteString(longDescription);
            writer.WriteEndElement();

            writer.WriteEndElement();
        }
    }

    //The depend element of the manifest file
    public class Dependencies : PackageElement
    {
        private readonly string[] buildTool;
        private readonly string[] build_exec;

        public Dependencies(String[] buildTool, String[] build_exec)
        {
            this.buildTool = buildTool;
            this.build_exec = build_exec;
        }

        public void WriteElement(XmlWriter writer)
        {
            foreach (String depend in buildTool)
            {
                writer.WriteStartElement("buildtool_depend");
                writer.WriteString(depend);
                writer.WriteEndElement();
            }

            foreach (String depend in build_exec)
            {
                writer.WriteStartElement("depend");
                writer.WriteString(depend);
                writer.WriteEndElement();
            }
        }
    }

    //The author element of the manifest file
    public class Author : PackageElement
    {
        private readonly string name;

        public Author(string name)
        {
            this.name = name;
        }

        public void WriteElement(XmlWriter writer)
        {
            writer.WriteStartElement("author");
            writer.WriteString(name);
            writer.WriteEndElement();

            writer.WriteStartElement("maintainer");
            writer.WriteAttributeString("email", name + "@email.com");
            writer.WriteEndElement();
        }
    }

    //The license element of the manifest file
    public class License : PackageElement
    {
        private readonly string lic;

        public License(string lic)
        {
            this.lic = lic;
        }

        public void WriteElement(XmlWriter writer)
        {
            writer.WriteStartElement("license");
            writer.WriteString(lic);
            writer.WriteEndElement();
        }
    }

    #region Windows Forms Derived classes

    //A LinkNode is derived from a TreeView TreeNode. I've added many new fields to it so that information can be passed around
    //from the TreeView itself.
    public class LinkNode : TreeNode
    {
        public Link Link
        { get; set; }

        public string LinkName
        { get; set; }

        public string JointName
        { get; set; }

        public string AxisName
        { get; set; }

        public string CoordsysName
        { get; set; }

        public List<Component2> Components;
        public List<byte[]> ComponentPIDs;

        public string JointType
        { get; set; }

        public bool IsBaseNode
        { get; set; }

        public bool IsIncomplete
        { get; set; }

        public bool NeedsSaving
        { get; set; }

        public string WhyIncomplete
        { get; set; }

        public LinkNode()
        {
        }

        public LinkNode(SerialNode node)
        {
            LinkName = node.linkName;
            JointName = node.jointName;
            AxisName = node.axisName;
            CoordsysName = node.coordsysName;
            ComponentPIDs = node.componentPIDs;
            JointType = node.jointType;
            IsBaseNode = node.isBaseNode;
            IsIncomplete = node.isIncomplete;

            Name = LinkName;
            Text = LinkName;

            foreach (SerialNode child in node.Nodes)
            {
                Nodes.Add(new LinkNode(child));
            }
        }
    }

    #endregion Windows Forms Derived classes
}