﻿using AutoMapper;
using EasyLOB.Data;
using System;
using System.Reflection;

namespace EasyLOB.Activity.Data
{
    public class ActivityDataAutoMapper : Profile
    {
        public ActivityDataAutoMapper()
        {
            Assembly dataAssembly = Assembly.GetExecutingAssembly();

            Type[] types = dataAssembly.GetTypes();
            foreach (Type type in types)
            {
                if (type.IsSubclassOf(typeof(ZDataBase)))
                {
                    string dto = type.FullName + "DTO";
                    Type typeDTO = dataAssembly.GetType(dto);

                    CreateMap(type, typeDTO, MemberList.None);
                    CreateMap(typeDTO, type, MemberList.None);
                }
            }
        }
    }
}