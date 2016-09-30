﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

namespace AESharp.Networking.Packets.Serialization
{
    internal sealed class ReflectedMember
    {
        public delegate void ValueSetterDelegate( object value, object instance );
        public delegate object ValueGetterDelegate( object instance );
        public delegate object ValueTransformerDelegate( object value );

        private static readonly Type[] TransformSignature;

        static ReflectedMember()
        {
            TransformSignature = new[] { typeof( object ) };
        }

        public BinaryConverterAttribute Converter { get; }
        public int? Length { get; }
        public Type Type { get; }
        public string Name { get; }

        public ValueSetterDelegate SetValue { get; }
        public ValueGetterDelegate GetValue { get; }
        public ReadOnlyCollection<ValueTransformerDelegate> Transformers { get; }

        public ReflectedMember( MemberInfo member )
        {
            this.Length = member.GetCustomAttribute<FixedLengthAttribute>()?.Length;

            var converter = member.GetCustomAttribute<BinaryConverterAttribute>();
            this.Converter = converter ?? new BinaryConverterAttribute( typeof( DefaultBinaryConverter ) );

            var transformerType = typeof( TransformAttribute );
            this.Transformers = member.GetCustomAttributes()
                                      .Select( a => a.GetType() )
                                      .Where( t => t.Inherits( transformerType ) )
                                      .Select( MakeTransformerDelegate )
                                      .ToList()
                                      .AsReadOnly();

            if( member is FieldInfo )
            {
                var field = member as FieldInfo;

                this.SetValue = field.SetValue;
                this.GetValue = field.GetValue;
                this.Type = field.FieldType;
                this.Name = field.Name;
            }
            else if( member is PropertyInfo )
            {
                var property = member as PropertyInfo;

                var getter = property.GetGetMethod( true );
                var setter = property.GetSetMethod( true );

                this.SetValue = ( value, instance ) => setter.Invoke( instance, new[] { value } );
                this.GetValue = ( instance ) => getter.Invoke( instance, null );
                this.Type = property.PropertyType;
                this.Name = property.Name;
            }
        }

        private static ValueTransformerDelegate MakeTransformerDelegate( Type type )
        {
            var instance = Activator.CreateInstance( type, true );
            var method = type.GetTypeInfo().GetMethod( "Transform", TransformSignature );

            return method.CreateDelegate( typeof( ValueTransformerDelegate ), instance ) as ValueTransformerDelegate;
        }
    }
}
